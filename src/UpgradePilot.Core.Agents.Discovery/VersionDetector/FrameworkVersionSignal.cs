namespace UpgradePilot.Core.Agents.Discovery.VersionDetector;

/// <summary>
/// One piece of version evidence pulled from a project file — a target framework
/// moniker or a known ABP/AspNet Zero package reference. Multiple signals from
/// different projects are aggregated into a <see cref="VersionManifest"/>; conflicting
/// signals are surfaces as-is rather than silently reconciled, per
/// docs/architecture/agents.md §4.2 ("on ambiguity, lower confidence rather than fail").
/// </summary>
public sealed record FrameworkVersionSignal(string Source, string Value, int Confidence);
