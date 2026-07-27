namespace UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

public sealed record FrameworkProfile(
    IReadOnlyList<FrameworkClassification> Classifications,
    bool HasAngularFrontEnd);
