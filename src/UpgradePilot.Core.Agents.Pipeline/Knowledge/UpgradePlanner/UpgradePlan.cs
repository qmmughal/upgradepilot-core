using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;

public sealed record UpgradePlan(
    IReadOnlyList<string> RemainingPipelineSteps,
    IReadOnlyList<RiskItem> RiskRegister,
    int ConfidenceScore,
    bool RequiresHumanApproval,
    StackKind StackKind = StackKind.Unknown,
    string RecommendedUpgradePath = "unknown-upgrade");
