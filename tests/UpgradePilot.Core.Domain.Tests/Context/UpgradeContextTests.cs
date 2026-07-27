using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Domain.Tests.Context;

public class UpgradeContextTests
{
    [Fact]
    public void Constructor_Throws_WhenSessionIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new UpgradeContext(Guid.Empty));
    }

    [Fact]
    public void RecordFact_AppendsFact_AndPreservesEarlierFacts()
    {
        var context = new UpgradeContext(Guid.NewGuid());

        context.RecordFact("repository-analyzer", "repository-map", new { Projects = 3 });
        context.RecordFact("version-detector", "version-manifest", new { DotNetVersion = "8.0" });

        Assert.Equal(2, context.Facts.Count);
        Assert.Equal("repository-analyzer", context.Facts[0].AgentId);
        Assert.Equal("version-detector", context.Facts[1].AgentId);
    }

    [Fact]
    public void LatestFact_ReturnsMostRecentByType_WhenAgentRecordsMultipleTimes()
    {
        var context = new UpgradeContext(Guid.NewGuid());

        context.RecordFact("build-validation-agent", "build-result", new { Passed = false, Attempt = 1 });
        context.RecordFact("build-validation-agent", "build-result", new { Passed = true, Attempt = 2 });

        var latest = context.LatestFact("build-result");

        Assert.NotNull(latest);
        Assert.Equal(2, context.FactsFrom("build-validation-agent").Count());
    }

    [Fact]
    public void LatestFact_ReturnsNull_WhenFactTypeNeverRecorded()
    {
        var context = new UpgradeContext(Guid.NewGuid());

        Assert.Null(context.LatestFact("nonexistent-fact-type"));
    }
}
