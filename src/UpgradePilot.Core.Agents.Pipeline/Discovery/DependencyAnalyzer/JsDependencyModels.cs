namespace UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;

public sealed record JsPackageDependency(string Id, string CurrentVersion, string LatestVersion);

public sealed record JsDependencyAnalysisResult(IReadOnlyList<JsPackageDependency> Outdated);
