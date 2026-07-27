namespace UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;

public sealed record ProjectInfo(
    string Name,
    string ProjectFilePath,
    IReadOnlyList<string> SourceFiles);
