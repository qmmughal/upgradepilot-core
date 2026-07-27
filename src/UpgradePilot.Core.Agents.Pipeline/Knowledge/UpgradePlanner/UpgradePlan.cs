namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;

public sealed record UpgradePlan(
    IReadOnlyList<string> RemainingPipelineSteps,
    IReadOnlyList<RiskItem> RiskRegister,
    int ConfidenceScore,
    bool RequiresHumanApproval);
