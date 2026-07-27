using UpgradePilot.Core.Agents.Pipeline.Discovery.RepositoryIntelligence;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Discovery;

public class RepositoryIntelligenceAgentTests
{
    private const string GitLogOutput =
        "COMMIT|abc123|Alice\n" +
        "10\t2\tsrc/Foo.cs\n" +
        "5\t0\tsrc/Bar.cs\n" +
        "COMMIT|def456|Bob\n" +
        "3\t1\tsrc/Foo.cs\n" +
        "COMMIT|ghi789|Alice\n" +
        "1\t1\tsrc/Foo.cs\n";

    [Fact]
    public async Task ExecuteAsync_AggregatesChurnAcrossCommits()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, GitLogOutput, ""));
        var agent = new RepositoryIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo", context);

        var fooChurn = result.Output.Heatmap.Churn.Single(c => c.FilePath == "src/Foo.cs");
        Assert.Equal(3, fooChurn.CommitCount); // touched in all 3 commits
        Assert.Equal(14, fooChurn.TotalInsertions); // 10 + 3 + 1
        Assert.Equal(4, fooChurn.TotalDeletions); // 2 + 1 + 1

        var barChurn = result.Output.Heatmap.Churn.Single(c => c.FilePath == "src/Bar.cs");
        Assert.Equal(1, barChurn.CommitCount);
    }

    [Fact]
    public async Task ExecuteAsync_AttributesPrimaryOwnershipByCommitFrequency()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, GitLogOutput, ""));
        var agent = new RepositoryIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo", context);

        var fooOwnership = result.Output.Ownership.Ownership.Single(o => o.FilePath == "src/Foo.cs");
        Assert.Equal("Alice", fooOwnership.PrimaryAuthor); // Alice touched Foo.cs in 2 of 3 commits
        Assert.Equal(2, fooOwnership.CommitsByPrimaryAuthor);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFacts_ForBothHeatmapAndOwnership()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, GitLogOutput, ""));
        var agent = new RepositoryIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        await agent.ExecuteAsync("/repo", context);

        Assert.NotNull(context.LatestFact("customization-heatmap"));
        Assert.NotNull(context.LatestFact("ownership-map"));
    }

    [Fact]
    public async Task ExecuteAsync_ZeroConfidence_WhenGitLogFails()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(128, "", "fatal: not a git repository"));
        var agent = new RepositoryIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/not-a-repo", context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.Heatmap.Churn);
    }

    [Fact]
    public async Task ExecuteAsync_RealGitLog_AgainstThisRepositorysOwnHistory()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var agent = new RepositoryIntelligenceAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(repoRoot, context);

        Assert.True(result.Output.Heatmap.Churn.Count > 0);
        Assert.True(result.Output.Ownership.Ownership.Count > 0);
        Assert.Contains(result.Output.Heatmap.Churn, c => c.FilePath.Contains("README.md"));
    }
}
