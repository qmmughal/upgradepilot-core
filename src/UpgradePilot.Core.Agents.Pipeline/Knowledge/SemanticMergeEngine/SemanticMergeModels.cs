using UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.SemanticMergeEngine;

public sealed record SemanticMergeInput(
    string CustomerSource,
    string TemplateBaselineSource,
    string TemplateTargetSource);

public sealed record MergeConflict(MemberSignature Signature, string Reason, string? CustomerVersion, string? TemplateVersion);

public sealed record MergeResult(string MergedSource, IReadOnlyList<MergeConflict> Conflicts);
