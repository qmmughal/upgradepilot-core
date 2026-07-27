using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.VersionDetector;

namespace UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

public sealed record FrameworkDetectorInput(RepositoryMap RepositoryMap, VersionManifest VersionManifest);
