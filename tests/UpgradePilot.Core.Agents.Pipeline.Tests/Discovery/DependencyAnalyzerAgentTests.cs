using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Discovery;

public class DependencyAnalyzerAgentTests
{
    private const string ListOutput = """
        {
          "version": 1,
          "parameters": "--include-transitive",
          "projects": [
            {
              "path": "/repo/Sample.Web/Sample.Web.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    { "id": "Abp.AspNetCore", "requestedVersion": "9.2.0", "resolvedVersion": "9.2.0" }
                  ],
                  "transitivePackages": [
                    { "id": "Newtonsoft.Json", "resolvedVersion": "13.0.3" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string VulnerableOutput = """
        {
          "version": 1,
          "parameters": "--vulnerable --include-transitive",
          "projects": [
            {
              "path": "/repo/Sample.Web/Sample.Web.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    {
                      "id": "Newtonsoft.Json",
                      "resolvedVersion": "9.0.1",
                      "vulnerabilities": [
                        { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-5crp-9r3c-p9vr" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string NoVulnerabilitiesOutput = """
        {
          "version": 1,
          "parameters": "--vulnerable --include-transitive",
          "sources": [ "https://api.nuget.org/v3/index.json" ],
          "projects": [
            { "path": "/repo/Sample.Web/Sample.Web.csproj" }
          ]
        }
        """;

    private const string NoOutdatedOutput = """
        {
          "version": 1,
          "parameters": "--outdated --include-transitive",
          "sources": [ "https://api.nuget.org/v3/index.json" ],
          "projects": [
            { "path": "/repo/Sample.Web/Sample.Web.csproj" }
          ]
        }
        """;

    private const string OutdatedOutput = """
        {
          "version": 1,
          "parameters": "--outdated --include-transitive",
          "projects": [
            {
              "path": "/repo/Sample.Web/Sample.Web.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    { "id": "Abp.AspNetCore", "requestedVersion": "9.2.0", "resolvedVersion": "9.2.0", "latestVersion": "9.3.1" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static readonly RepositoryMap SampleMap = new(
        "/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);

    private sealed class SequencedProcessRunner(params ProcessRunResult[] results) : IProcessRunner
    {
        private int _index;

        public Task<ProcessRunResult> RunAsync(
            string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(results[_index++]);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesDirectAndTransitivePackages()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, ListOutput, ""),
            new ProcessRunResult(0, NoOutdatedOutput, ""),
            new ProcessRunResult(0, NoVulnerabilitiesOutput, ""));
        var agent = new DependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(SampleMap, context);

        var projectDeps = Assert.Single(result.Output.Graph.Projects);
        Assert.Contains(projectDeps.Packages, p => p.Id == "Abp.AspNetCore" && p.IsDirect);
        Assert.Contains(projectDeps.Packages, p => p.Id == "Newtonsoft.Json" && !p.IsDirect);
        Assert.Empty(result.Output.RiskReport.VulnerablePackages);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_FlagsVulnerablePackages()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, ListOutput, ""),
            new ProcessRunResult(0, NoOutdatedOutput, ""),
            new ProcessRunResult(0, VulnerableOutput, ""));
        var agent = new DependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(SampleMap, context);

        var vuln = Assert.Single(result.Output.RiskReport.VulnerablePackages);
        Assert.Equal("Newtonsoft.Json", vuln.PackageId);
        Assert.Equal("High", vuln.Severity);
        Assert.Equal("Sample.Web", vuln.ProjectName);
    }

    [Fact]
    public async Task ExecuteAsync_PopulatesLatestVersion_ForOutdatedPackages()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, ListOutput, ""),
            new ProcessRunResult(0, OutdatedOutput, ""),
            new ProcessRunResult(0, NoVulnerabilitiesOutput, ""));
        var agent = new DependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(SampleMap, context);

        var projectDeps = Assert.Single(result.Output.Graph.Projects);
        var abp = Assert.Single(projectDeps.Packages, p => p.Id == "Abp.AspNetCore");
        Assert.Equal("9.3.1", abp.LatestVersion);

        var newtonsoft = Assert.Single(projectDeps.Packages, p => p.Id == "Newtonsoft.Json");
        Assert.Null(newtonsoft.LatestVersion);
    }

    [Fact]
    public async Task ExecuteAsync_MarksProjectUnresolved_WhenListOutputIsNotJson()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(1, "error NU1301: unable to restore", ""));
        var agent = new DependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(SampleMap, context);

        Assert.Contains("Sample.Web", result.Output.Graph.UnresolvedProjectNames);
        Assert.Equal(40, result.Confidence);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenAnyProjectUnresolved()
    {
        var agent = new DependencyAnalyzerAgent(new SequencedProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());
        var output = new DependencyAnalysisResult(
            new DependencyGraph([], ["Sample.Web"]), new DependencyRiskReport([]));

        var validation = await agent.ValidateAsync(output, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealDotnetList_AgainstOwnTestProject_FindsKnownPackages()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "tests", "UpgradePilot.Core.Domain.Tests", "UpgradePilot.Core.Domain.Tests.csproj");
        var map = new RepositoryMap(repoRoot, [new ProjectInfo("UpgradePilot.Core.Domain.Tests", projectPath, [])]);

        var agent = new DependencyAnalyzerAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(map, context);

        Assert.Empty(result.Output.Graph.UnresolvedProjectNames);
        var projectDeps = Assert.Single(result.Output.Graph.Projects);
        Assert.Contains(projectDeps.Packages, p => p.Id == "xunit" && p.IsDirect);
    }
}
