using UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.TemplateComparator;

/// <summary>Single-file v0.1 scope - see RoslynMemberComparator's doc comment for why.</summary>
public sealed record TemplateComparatorInput(
    string CustomerSource,
    string TemplateBaselineSource,
    string TemplateTargetSource);

public sealed record TemplateComparatorResult(
    AstDiffResult CustomizationSet,
    AstDiffResult TemplateChangeSet);
