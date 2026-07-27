using System.Text;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Delivery.DocumentationGenerator;

/// <summary>
/// Agent #18 (docs/architecture/agents.md §4.18): renders the upgrade report from the
/// session's recorded facts. Deliberately pure/deterministic templating rather than an
/// LLM call, so the spec's validation rule ("every claim must trace to a fact in
/// UpgradeContext - no unsourced content") is enforceable by construction: the report
/// can only ever contain what's already in <see cref="UpgradeContext.Facts"/>.
/// </summary>
public sealed class DocumentationGeneratorAgent : IUpgradePilotAgent<string, string>
{
    public string AgentId => "documentation-generator";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<string>> ExecuteAsync(
        string reportTitle, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var priorFacts = context.Facts.ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {reportTitle}");
        sb.AppendLine();
        sb.AppendLine($"Session: `{context.SessionId}`");
        sb.AppendLine();
        sb.AppendLine("## Facts recorded");
        sb.AppendLine();

        foreach (var fact in priorFacts)
        {
            sb.AppendLine($"- **{fact.RecordedAt:O}** `{fact.AgentId}` -> `{fact.FactType}`");
        }

        var report = sb.ToString();
        context.RecordFact(AgentId, "upgrade-report", report);

        var result = AgentResult<string>.Create(
            report,
            confidence: 100,
            explanation: $"Rendered report from {priorFacts.Count} recorded fact(s).",
            citations: priorFacts.Select(f => new Citation($"{f.AgentId}:{f.FactType}")).ToList());

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        string output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(context.Facts
            .Where(f => f.AgentId != AgentId) // exclude the report's own fact, recorded after the report body was built
            .All(f => output.Contains(f.FactType, StringComparison.Ordinal))
            ? ValidationResult.Success()
            : ValidationResult.Failure("Report is missing a citation for at least one recorded fact."));
}
