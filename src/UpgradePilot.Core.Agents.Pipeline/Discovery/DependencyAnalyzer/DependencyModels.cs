namespace UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;

public sealed record PackageDependency(string Id, string ResolvedVersion, bool IsDirect);

public sealed record ProjectDependencies(string ProjectName, IReadOnlyList<PackageDependency> Packages);

public sealed record DependencyGraph(
    IReadOnlyList<ProjectDependencies> Projects,
    IReadOnlyList<string> UnresolvedProjectNames);

public sealed record VulnerablePackage(
    string ProjectName, string PackageId, string ResolvedVersion, string Severity, string? AdvisoryUrl);

public sealed record DependencyRiskReport(IReadOnlyList<VulnerablePackage> VulnerablePackages);

public sealed record DependencyAnalysisResult(DependencyGraph Graph, DependencyRiskReport RiskReport);
