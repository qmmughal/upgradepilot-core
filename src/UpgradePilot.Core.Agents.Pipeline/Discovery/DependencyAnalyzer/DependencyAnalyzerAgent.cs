using System.Text.Json;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;

/// <summary>
/// Agent #4 (docs/architecture/agents.md §4.4): resolves the dependency graph and
/// flags known-vulnerable packages via real `dotnet list package --include-transitive`
/// / `--vulnerable` calls against nuget.org (through the IProcessRunner port). Lives in
/// Agents.Pipeline rather than Agents.Discovery purely to avoid a circular project
/// reference (Discovery has no dependency on Pipeline's IProcessRunner) - it is still
/// conceptually a Discovery-phase agent and its Input is RepositoryMap, same as its
/// siblings.
/// </summary>
public sealed class DependencyAnalyzerAgent : IUpgradePilotAgent<RepositoryMap, DependencyAnalysisResult>
{
    private readonly IProcessRunner _processRunner;

    public DependencyAnalyzerAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "dependency-analyzer";
    public string Version => "0.1.0";

    /// <summary>2 attempts on registry timeouts, per spec §4.4.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: false);

    public async Task<AgentResult<DependencyAnalysisResult>> ExecuteAsync(
        RepositoryMap input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var resolvedProjects = new List<ProjectDependencies>();
        var unresolvedProjects = new List<string>();
        var vulnerablePackages = new List<VulnerablePackage>();

        foreach (var project in input.Projects)
        {
            var workingDirectory = Path.GetDirectoryName(project.ProjectFilePath) ?? Directory.GetCurrentDirectory();

            var listRun = await _processRunner.RunAsync(
                "dotnet", $"list \"{project.ProjectFilePath}\" package --include-transitive --format json",
                workingDirectory, cancellationToken);

            var packages = TryParsePackages(listRun.StandardOutput);
            if (packages is null)
            {
                unresolvedProjects.Add(project.Name);
                continue;
            }

            var outdatedRun = await _processRunner.RunAsync(
                "dotnet", $"list \"{project.ProjectFilePath}\" package --outdated --include-transitive --format json",
                workingDirectory, cancellationToken);

            var latestVersionsById = TryParseLatestVersions(outdatedRun.StandardOutput);
            var packagesWithLatest = packages
                .Select(p => latestVersionsById.TryGetValue(p.Id, out var latest) ? p with { LatestVersion = latest } : p)
                .ToList();

            resolvedProjects.Add(new ProjectDependencies(project.Name, packagesWithLatest));

            var vulnRun = await _processRunner.RunAsync(
                "dotnet", $"list \"{project.ProjectFilePath}\" package --vulnerable --include-transitive --format json",
                workingDirectory, cancellationToken);

            vulnerablePackages.AddRange(TryParseVulnerabilities(project.Name, vulnRun.StandardOutput));
        }

        var graph = new DependencyGraph(resolvedProjects, unresolvedProjects);
        var riskReport = new DependencyRiskReport(vulnerablePackages);
        var analysisResult = new DependencyAnalysisResult(graph, riskReport);

        context.RecordFact(AgentId, "dependency-graph", graph);
        context.RecordFact(AgentId, "dependency-risk-report", riskReport);

        var explanation = unresolvedProjects.Count == 0
            ? $"Resolved dependencies for all {resolvedProjects.Count} project(s); {vulnerablePackages.Count} known-vulnerable package reference(s) found."
            : $"Could not resolve dependencies for {unresolvedProjects.Count} project(s) (likely not restored): {string.Join(", ", unresolvedProjects)}.";

        var confidence = input.Projects.Count == 0 ? 0 : unresolvedProjects.Count == 0 ? 90 : 40;

        var result = AgentResult<DependencyAnalysisResult>.Create(
            analysisResult, confidence, explanation, citations: [new Citation("dotnet list package")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        DependencyAnalysisResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Graph.UnresolvedProjectNames.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(
                $"{output.Graph.UnresolvedProjectNames.Count} project(s) could not be resolved: {string.Join(", ", output.Graph.UnresolvedProjectNames)}."));

    private static List<PackageDependency>? TryParsePackages(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.GetArrayLength() == 0)
            {
                return [];
            }

            var packages = new List<PackageDependency>();
            var project = projects[0];
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                return packages;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                AppendPackages(framework, "topLevelPackages", isDirect: true, packages);
                AppendPackages(framework, "transitivePackages", isDirect: false, packages);
            }

            return packages;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AppendPackages(JsonElement framework, string propertyName, bool isDirect, List<PackageDependency> into)
    {
        if (!framework.TryGetProperty(propertyName, out var array))
        {
            return;
        }

        foreach (var pkg in array.EnumerateArray())
        {
            var id = pkg.GetProperty("id").GetString() ?? "unknown";
            var version = pkg.TryGetProperty("resolvedVersion", out var rv) ? rv.GetString() ?? "unknown" : "unknown";
            into.Add(new PackageDependency(id, version, isDirect));
        }
    }

    /// <summary>`dotnet list package --outdated` only lists packages that DO have a newer version - absence from this map is itself the "already current" signal, not a gap.</summary>
    private static Dictionary<string, string> TryParseLatestVersions(string json)
    {
        var latestVersionsById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.GetArrayLength() == 0)
            {
                return latestVersionsById;
            }

            var project = projects[0];
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                return latestVersionsById;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                foreach (var propertyName in new[] { "topLevelPackages", "transitivePackages" })
                {
                    if (!framework.TryGetProperty(propertyName, out var array))
                    {
                        continue;
                    }

                    foreach (var pkg in array.EnumerateArray())
                    {
                        if (!pkg.TryGetProperty("latestVersion", out var latest))
                        {
                            continue;
                        }

                        var id = pkg.GetProperty("id").GetString();
                        var latestVersion = latest.GetString();
                        if (id is not null && latestVersion is not null)
                        {
                            latestVersionsById[id] = latestVersion;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // no outdated-package data available - not fatal, target versions simply stay unresolved
        }

        return latestVersionsById;
    }

    private static List<VulnerablePackage> TryParseVulnerabilities(string projectName, string json)
    {
        var results = new List<VulnerablePackage>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.GetArrayLength() == 0)
            {
                return results;
            }

            var project = projects[0];
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                return results;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                CollectVulnerable(framework, "topLevelPackages", projectName, results);
                CollectVulnerable(framework, "transitivePackages", projectName, results);
            }
        }
        catch (JsonException)
        {
            // no vulnerability data available for this project - not fatal, just an empty result
        }

        return results;
    }

    private static void CollectVulnerable(JsonElement framework, string propertyName, string projectName, List<VulnerablePackage> into)
    {
        if (!framework.TryGetProperty(propertyName, out var array))
        {
            return;
        }

        foreach (var pkg in array.EnumerateArray())
        {
            if (!pkg.TryGetProperty("vulnerabilities", out var vulnerabilities))
            {
                continue;
            }

            var id = pkg.GetProperty("id").GetString() ?? "unknown";
            var version = pkg.TryGetProperty("resolvedVersion", out var rv) ? rv.GetString() ?? "unknown" : "unknown";

            foreach (var vuln in vulnerabilities.EnumerateArray())
            {
                var severity = vuln.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "unknown" : "unknown";
                var advisoryUrl = vuln.TryGetProperty("advisoryurl", out var url) ? url.GetString() : null;
                into.Add(new VulnerablePackage(projectName, id, version, severity, advisoryUrl));
            }
        }
    }
}
