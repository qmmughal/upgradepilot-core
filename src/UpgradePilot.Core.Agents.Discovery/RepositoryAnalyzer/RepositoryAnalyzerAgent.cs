using UpgradePilot.Core.Agents.Discovery.Ports;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;

/// <summary>
/// Agent #1 (docs/architecture/agents.md §4.1): builds a structural map of the target
/// repository — enumerates projects and their source files. First step in the
/// Discovery phase; every other Discovery agent consumes its <see cref="RepositoryMap"/>.
/// </summary>
public sealed class RepositoryAnalyzerAgent : IUpgradePilotAgent<string, RepositoryMap>
{
    private readonly IRepositoryReader _reader;

    public RepositoryAnalyzerAgent(IRepositoryReader reader)
    {
        _reader = reader;
    }

    public string AgentId => "repository-analyzer";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(1), UseExponentialBackoff: true);

    public Task<AgentResult<RepositoryMap>> ExecuteAsync(
        string rootPath, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var projects = _reader.EnumerateFiles(rootPath, "*.csproj")
            .Select(projectFilePath => new ProjectInfo(
                Name: Path.GetFileNameWithoutExtension(projectFilePath),
                ProjectFilePath: projectFilePath,
                SourceFiles: EnumerateSourceFiles(Path.GetDirectoryName(projectFilePath)!).ToList()))
            .ToList();

        var map = new RepositoryMap(rootPath, projects);
        context.RecordFact(AgentId, "repository-map", map);

        var result = projects.Count > 0
            ? AgentResult<RepositoryMap>.Create(
                map,
                confidence: 100,
                explanation: $"Found {projects.Count} project(s) under '{rootPath}'.",
                citations: [new Citation("Local filesystem scan (*.csproj)")])
            : AgentResult<RepositoryMap>.Create(
                map,
                confidence: 0,
                explanation: $"No .csproj files found under '{rootPath}'.");

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        RepositoryMap output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Projects.Count > 0
            ? ValidationResult.Success()
            : ValidationResult.Failure("At least one recognizable project file must be found."));

    private IEnumerable<string> EnumerateSourceFiles(string projectDirectory) =>
        _reader.EnumerateFiles(projectDirectory, "*.cs")
            .Where(path => !IsBuildOutputPath(path));

    private static bool IsBuildOutputPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }
}
