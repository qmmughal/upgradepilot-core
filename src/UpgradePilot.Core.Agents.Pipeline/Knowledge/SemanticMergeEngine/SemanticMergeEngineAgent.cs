using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.SemanticMergeEngine;

/// <summary>
/// Agent #10 (docs/architecture/agents.md §4.10): three-way AST merge - starts from the
/// customer's file (so their customizations are the default), propagates template
/// changes for members the customer never touched, and raises a conflict (never a
/// silent overwrite) whenever the customer and the template changed - or one changed
/// and the other removed - the same member. Built on the same Roslyn member-signature
/// model as the Template Comparator; not validated against real AspNet Zero repos (see
/// TemplateComparatorAgent's doc comment - same caveat applies here).
/// </summary>
public sealed class SemanticMergeEngineAgent : IUpgradePilotAgent<SemanticMergeInput, MergeResult>
{
    public string AgentId => "semantic-merge-engine";
    public string Version => "0.1.0";

    /// <summary>Not retried - a merge conflict is a planning input, not a transient failure, per spec §4.10.</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<MergeResult>> ExecuteAsync(
        SemanticMergeInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var customizationSet = RoslynMemberComparator.Diff(input.TemplateBaselineSource, input.CustomerSource);
        var templateChangeSet = RoslynMemberComparator.Diff(input.TemplateBaselineSource, input.TemplateTargetSource);

        var customerType = RoslynMemberComparator.ExtractMembers(input.CustomerSource);
        var customizationBySignature = customizationSet.MemberDiffs.ToDictionary(d => d.Signature);

        var mergedOrder = customerType.Members.Select(m => m.Signature).ToList();
        var mergedText = customerType.Members.ToDictionary(m => m.Signature, m => m.Text);
        var conflicts = new List<MergeConflict>();

        foreach (var templateChange in templateChangeSet.MemberDiffs)
        {
            if (templateChange.ChangeKind == MemberChangeKind.Unchanged)
            {
                continue;
            }

            var custChange = customizationBySignature.GetValueOrDefault(templateChange.Signature);
            var customerTouchedIt = custChange is not null && custChange.ChangeKind != MemberChangeKind.Unchanged;

            switch (templateChange.ChangeKind)
            {
                case MemberChangeKind.Added when mergedText.ContainsKey(templateChange.Signature):
                    if (mergedText[templateChange.Signature] != templateChange.OtherText)
                    {
                        conflicts.Add(new MergeConflict(
                            templateChange.Signature,
                            "Template and customer both added a member with this signature, with different implementations.",
                            mergedText[templateChange.Signature], templateChange.OtherText));
                    }

                    break;

                case MemberChangeKind.Added:
                    mergedText[templateChange.Signature] = templateChange.OtherText!;
                    mergedOrder.Add(templateChange.Signature);
                    break;

                case MemberChangeKind.Modified when !customerTouchedIt:
                    mergedText[templateChange.Signature] = templateChange.OtherText!;
                    break;

                case MemberChangeKind.Modified:
                    conflicts.Add(new MergeConflict(
                        templateChange.Signature,
                        "Both the template and the customer modified this member.",
                        mergedText.GetValueOrDefault(templateChange.Signature), templateChange.OtherText));
                    break;

                case MemberChangeKind.Removed when !customerTouchedIt:
                    mergedText.Remove(templateChange.Signature);
                    mergedOrder.Remove(templateChange.Signature);
                    break;

                case MemberChangeKind.Removed:
                    conflicts.Add(new MergeConflict(
                        templateChange.Signature,
                        "The template removed this member, but the customer customized it.",
                        mergedText.GetValueOrDefault(templateChange.Signature), null));
                    break;
            }
        }

        var mergedSource = RebuildSource(input.CustomerSource, mergedOrder.Where(mergedText.ContainsKey).Select(s => mergedText[s]));
        var mergeResult = new MergeResult(mergedSource, conflicts);

        context.RecordFact(AgentId, "merge-result", mergeResult);

        var result = conflicts.Count == 0
            ? AgentResult<MergeResult>.Create(
                mergeResult, 90, "Merge completed with no conflicts.",
                citations: [new Citation("Roslyn three-way member merge")])
            : AgentResult<MergeResult>.Create(
                mergeResult, 40, $"Merge completed with {conflicts.Count} conflict(s) requiring human review.",
                citations: conflicts.Select(c => new Citation($"{c.Signature}: {c.Reason}")).ToList());

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        MergeResult output, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(output.MergedSource);
        var hasSyntaxErrors = tree.GetDiagnostics().Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        return Task.FromResult(hasSyntaxErrors
            ? ValidationResult.Failure("Merged source failed to parse without syntax errors.")
            : ValidationResult.Success());
    }

    private static string RebuildSource(string customerSource, IEnumerable<string> mergedMemberTexts)
    {
        var tree = CSharpSyntaxTree.ParseText(customerSource);
        var root = tree.GetCompilationUnitRoot();
        var typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First();

        var parsedMembers = mergedMemberTexts
            .Select(text => SyntaxFactory.ParseMemberDeclaration(text)
                ?? throw new InvalidOperationException($"Could not parse merged member: {text}"));

        var updatedTypeDecl = typeDecl.WithMembers(SyntaxFactory.List(parsedMembers));
        var updatedRoot = root.ReplaceNode(typeDecl, updatedTypeDecl);

        return updatedRoot.NormalizeWhitespace().ToFullString();
    }
}
