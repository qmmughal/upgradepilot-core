using UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.TemplateComparator;

/// <summary>
/// Agent #9 (docs/architecture/agents.md §4.9): separates "what the customer changed"
/// from "what the template changed" - the core mechanism protecting customer business
/// logic. Real Roslyn AST-level member diff (RoslynMemberComparator), not text/regex.
///
/// This is the agent issue #1's spike was meant to validate against real, messy
/// AspNet Zero repos before building - no such repo is available in this environment,
/// so this implementation has been validated only against synthetic scenarios (see
/// tests). The real-world conflict rate on actual customized repos remains unknown;
/// treat this as a tested engine awaiting real-world validation, not a closed spike.
/// </summary>
public sealed class TemplateComparatorAgent : IUpgradePilotAgent<TemplateComparatorInput, TemplateComparatorResult>
{
    public string AgentId => "template-comparator";
    public string Version => "0.1.0";

    /// <summary>No retry on parse failure - an unparseable file needs a human, not a retry, per spec §4.9.</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<TemplateComparatorResult>> ExecuteAsync(
        TemplateComparatorInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var customizationSet = RoslynMemberComparator.Diff(input.TemplateBaselineSource, input.CustomerSource);
        var templateChangeSet = RoslynMemberComparator.Diff(input.TemplateBaselineSource, input.TemplateTargetSource);

        var comparatorResult = new TemplateComparatorResult(customizationSet, templateChangeSet);

        context.RecordFact(AgentId, "customization-set", customizationSet);
        context.RecordFact(AgentId, "template-change-set", templateChangeSet);

        var customizedMemberCount = customizationSet.MemberDiffs.Count(d => d.ChangeKind != MemberChangeKind.Unchanged);
        var templateChangedMemberCount = templateChangeSet.MemberDiffs.Count(d => d.ChangeKind != MemberChangeKind.Unchanged);

        var result = AgentResult<TemplateComparatorResult>.Create(
            comparatorResult,
            confidence: 90,
            explanation: $"Found {customizedMemberCount} customer-changed member(s) and "
                + $"{templateChangedMemberCount} template-changed member(s), by Roslyn AST comparison "
                + "against the version baseline.",
            citations: [new Citation("Roslyn syntax tree member comparison")]);

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        TemplateComparatorResult output, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var hasDuplicateSignature =
            HasDuplicateSignatures(output.CustomizationSet) || HasDuplicateSignatures(output.TemplateChangeSet);

        return Task.FromResult(hasDuplicateSignature
            ? ValidationResult.Failure("A diff result contains two entries for the same member signature - every member must land in exactly one bucket.")
            : ValidationResult.Success());
    }

    private static bool HasDuplicateSignatures(AstDiffResult diff) =>
        diff.MemberDiffs.Select(d => d.Signature).Distinct().Count() != diff.MemberDiffs.Count;
}
