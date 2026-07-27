using UpgradePilot.Core.Agents.Pipeline.Knowledge.ReleaseNotesIntelligence;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class ReleaseNotesIntelligenceAgentTests
{
    private const string ReleasesJson = """
        [
          {
            "tag_name": "2.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/2.0.0",
            "body": "* BREAKING: renamed IFoo to IBar\n* Fix null reference in startup\n* Added new caching feature\n* Deprecated the old Widget API\n* Updated docs"
          }
        ]
        """;

    [Fact]
    public async Task ExecuteAsync_ClassifiesEachBulletByKeyword()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, ReleasesJson, ""));
        var agent = new ReleaseNotesIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new ReleaseNotesInput("example", "repo"), context);

        Assert.Contains(result.Output.Items, i => i.Category == ChangeCategory.Breaking && i.Description.Contains("IBar"));
        Assert.Contains(result.Output.Items, i => i.Category == ChangeCategory.Fix);
        Assert.Contains(result.Output.Items, i => i.Category == ChangeCategory.Feature);
        Assert.Contains(result.Output.Items, i => i.Category == ChangeCategory.Deprecation);
    }

    [Fact]
    public async Task ExecuteAsync_EveryItemCitesSourceReleaseUrl()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, ReleasesJson, ""));
        var agent = new ReleaseNotesIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new ReleaseNotesInput("example", "repo"), context);

        Assert.All(result.Output.Items, i => Assert.Equal("https://github.com/example/repo/releases/tag/2.0.0", i.SourceUrl));
    }

    [Fact]
    public async Task ExecuteAsync_ZeroConfidence_WhenGhApiFails()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, "", "HTTP 404: Not Found"));
        var agent = new ReleaseNotesIntelligenceAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new ReleaseNotesInput("no", "such-repo"), context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenAnyItemMissingSourceUrl()
    {
        var agent = new ReleaseNotesIntelligenceAgent(new FakeProcessRunner(new ProcessRunResult(0, "[]", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var badLedger = new BreakingChangeLedger([new BreakingChangeLedgerItem("1.0", "desc", ChangeCategory.Other, "")]);

        var validation = await agent.ValidateAsync(badLedger, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealGhApi_FetchesRealReleasesFromAbpFramework()
    {
        var agent = new ReleaseNotesIntelligenceAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new ReleaseNotesInput("abpframework", "abp", MaxReleases: 3), context);

        Assert.True(result.Output.Items.Count > 0);
        Assert.All(result.Output.Items, i => Assert.StartsWith("https://github.com/abpframework/abp", i.SourceUrl));
    }
}
