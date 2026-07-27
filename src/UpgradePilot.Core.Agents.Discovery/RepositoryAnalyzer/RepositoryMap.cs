namespace UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;

public sealed record RepositoryMap(string RootPath, IReadOnlyList<ProjectInfo> Projects);
