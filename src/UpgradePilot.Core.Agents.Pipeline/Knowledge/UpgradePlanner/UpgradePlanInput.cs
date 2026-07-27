using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.VersionDetector;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;

public sealed record UpgradePlanInput(
    RepositoryMap RepositoryMap,
    VersionManifest VersionManifest,
    FrameworkProfile FrameworkProfile);
