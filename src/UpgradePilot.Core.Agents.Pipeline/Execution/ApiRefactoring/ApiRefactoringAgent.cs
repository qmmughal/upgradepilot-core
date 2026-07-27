using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

/// <summary>
/// Agent #13 (docs/architecture/agents.md §4.13): applies source-level fixes for
/// breaking API changes, keyed to Release Notes Intelligence's ledger (#7 - real, but
/// heuristic-classified, so not yet precise enough to drive codemod selection
/// automatically). This agent's real capability is the rename codemod itself: a
/// Roslyn SyntaxRewriter identifier-token rename, never regex, per the "prefer AST
/// transformations" principle. v0.1 scope: syntactic (identifier-text) matching, not
/// symbol/semantic-model resolution - it will rename an unrelated identifier that
/// happens to share the same text (e.g. a local variable), which a full semantic
/// rename (requiring a Compilation with references) would not. Documented, not hidden.
/// </summary>
public sealed class ApiRefactoringAgent : IUpgradePilotAgent<ApiRefactoringInput, RefactoringReport>
{
    public string AgentId => "api-refactoring-agent";
    public string Version => "0.1.0";

    /// <summary>Bounded retry loop with Build Validation Agent, max 3 rounds per spec §4.13.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.Zero, UseExponentialBackoff: false);

    public Task<AgentResult<RefactoringReport>> ExecuteAsync(
        ApiRefactoringInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var source = File.ReadAllText(input.SourcePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var renameMap = input.Renames.ToDictionary(r => r.OldName, r => r.NewName);
        var rewriter = new RenameRewriter(renameMap);
        var newRoot = rewriter.Visit(root);

        var refactoredSource = newRoot.ToFullString();

        var changes = input.Renames
            .Select(r => new RefactoringChange(r.OldName, r.NewName, rewriter.Counts.GetValueOrDefault(r.OldName)))
            .Where(c => c.OccurrencesReplaced > 0)
            .ToList();

        var report = new RefactoringReport(refactoredSource, changes);
        context.RecordFact(AgentId, "refactoring-report", report);

        var totalOccurrences = changes.Sum(c => c.OccurrencesReplaced);
        var result = AgentResult<RefactoringReport>.Create(
            report, 80,
            $"Applied {changes.Count} rename rule(s) across {totalOccurrences} identifier occurrence(s).",
            citations: changes.Select(c => new Citation($"Rename rule: {c.OldName} -> {c.NewName}")).ToList());

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        RefactoringReport output, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(output.RefactoredSource);
        var hasSyntaxErrors = tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);

        return Task.FromResult(hasSyntaxErrors
            ? ValidationResult.Failure("Refactored source failed to parse without syntax errors.")
            : ValidationResult.Success());
    }

    private sealed class RenameRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _renames;
        private readonly Dictionary<string, int> _counts = [];

        public RenameRewriter(Dictionary<string, string> renames)
        {
            _renames = renames;
        }

        public IReadOnlyDictionary<string, int> Counts => _counts;

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && _renames.TryGetValue(token.Text, out var newName))
            {
                _counts[token.Text] = _counts.GetValueOrDefault(token.Text) + 1;
                return SyntaxFactory.Identifier(token.LeadingTrivia, newName, token.TrailingTrivia);
            }

            return base.VisitToken(token);
        }
    }
}
