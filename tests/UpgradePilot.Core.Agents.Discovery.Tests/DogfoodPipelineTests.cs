using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.Ports;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.VersionDetector;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.Tests;

/// <summary>
/// Runs the real Discovery-phase pipeline (Repository Analyzer -> Version Detector ->
/// Framework Detector) against upgradepilot-core's own solution, since no external
/// AspNet Zero sample repo is available yet. Proves the pipeline works end-to-end
/// against a real, non-trivial .NET repo rather than only synthetic fixtures.
/// </summary>
public class DogfoodPipelineTests
{
    [Fact]
    public async Task Pipeline_RunsAgainstThisRepository_AndFindsKnownProjects()
    {
        var repoRoot = FindRepositoryRoot();
        var reader = new LocalFileSystemRepositoryReader();
        var context = new UpgradeContext(Guid.NewGuid());

        var analyzerResult = await new RepositoryAnalyzerAgent(reader).ExecuteAsync(repoRoot, context);
        Assert.True(analyzerResult.Output.Projects.Count >= 4, "Expected to find at least the Domain, Domain.Tests, Agents.Discovery, and Agents.Discovery.Tests projects.");
        Assert.Contains(analyzerResult.Output.Projects, p => p.Name == "UpgradePilot.Core.Domain");

        var versionResult = await new VersionDetectorAgent(reader).ExecuteAsync(analyzerResult.Output, context);
        Assert.Contains(versionResult.Output.Signals, s => s.Value == "net10.0");

        var frameworkResult = await new FrameworkDetectorAgent(reader)
            .ExecuteAsync(new FrameworkDetectorInput(analyzerResult.Output, versionResult.Output), context);
        Assert.Equal(analyzerResult.Output.Projects.Count, frameworkResult.Output.Classifications.Count);

        Assert.Equal(3, context.FactsFrom("repository-analyzer").Count() + context.FactsFrom("version-detector").Count() + context.FactsFrom("framework-detector").Count());
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !dir.GetFiles("UpgradePilot.slnx").Any())
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root (UpgradePilot.slnx) from " + AppContext.BaseDirectory);
    }
}
