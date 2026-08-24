namespace UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;

/// <summary>
/// <see cref="LatestVersion"/> is null when `dotnet list package --outdated` doesn't
/// report this package - which is how the CLI itself signals "no newer version is
/// available", not a parsing gap.
/// </summary>
public sealed record PackageDependency(string Id, string ResolvedVersion, bool IsDirect, string? LatestVersion = null);

public sealed record ProjectDependencies(string ProjectName, IReadOnlyList<PackageDependency> Packages);

public sealed record DependencyGraph(
    IReadOnlyList<ProjectDependencies> Projects,
    IReadOnlyList<string> UnresolvedProjectNames);

public sealed record VulnerablePackage(
    string ProjectName, string PackageId, string ResolvedVersion, string Severity, string? AdvisoryUrl);

public sealed record DependencyRiskReport(IReadOnlyList<VulnerablePackage> VulnerablePackages);

public sealed record DependencyAnalysisResult(DependencyGraph Graph, DependencyRiskReport RiskReport);
