namespace UpgradePilot.Core.Agents.Discovery.VersionDetector;

public sealed record VersionManifest(IReadOnlyList<FrameworkVersionSignal> Signals);
