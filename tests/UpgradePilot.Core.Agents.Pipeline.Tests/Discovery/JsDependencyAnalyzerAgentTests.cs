using UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Discovery;

public class JsDependencyAnalyzerAgentTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-js-dep-analyzer-" + Guid.NewGuid());

    private const string OutdatedOutput = """
        {
          "is-number": {
            "current": "6.0.0",
            "wanted": "6.0.0",
            "latest": "7.0.0",
            "dependent": "fixture",
            "location": "node_modules/is-number"
          }
        }
        """;

    public JsDependencyAnalyzerAgentTests()
    {
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesOutdatedPackages_EvenThoughNpmExitsNonZero()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, OutdatedOutput, ""));
        var agent = new JsDependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(_fixtureDir, context);

        var entry = Assert.Single(result.Output.Outdated);
        Assert.Equal("is-number", entry.Id);
        Assert.Equal("6.0.0", entry.CurrentVersion);
        Assert.Equal("7.0.0", entry.LatestVersion);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsNoOutdatedPackages_WhenEverythingIsCurrent()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "", ""));
        var agent = new JsDependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(_fixtureDir, context);

        Assert.Empty(result.Output.Outdated);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsZeroConfidence_WhenOutputIsNotJson()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(254, "npm ERR! network timeout", "network timeout"));
        var agent = new JsDependencyAnalyzerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(_fixtureDir, context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.Outdated);
    }

    [Fact]
    public async Task ExecuteAsync_RealNpmOutdated_AgainstRealFixture_FindsKnownOutdatedPackage()
    {
        await File.WriteAllTextAsync(Path.Combine(_fixtureDir, "package.json"), """
            { "name": "fixture", "version": "1.0.0", "dependencies": { "is-number": "^6.0.0" } }
            """);
        await new SystemProcessRunner().RunAsync("npm", "install", _fixtureDir);

        var agent = new JsDependencyAnalyzerAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(_fixtureDir, context);

        Assert.Contains(result.Output.Outdated, p => p.Id == "is-number" && p.LatestVersion == "7.0.0");
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDir))
        {
            Directory.Delete(_fixtureDir, recursive: true);
        }
    }
}
