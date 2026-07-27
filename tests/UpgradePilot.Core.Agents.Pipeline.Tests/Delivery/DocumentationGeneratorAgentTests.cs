using UpgradePilot.Core.Agents.Pipeline.Delivery.DocumentationGenerator;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Delivery;

public class DocumentationGeneratorAgentTests
{
    [Fact]
    public async Task ExecuteAsync_RendersAllPriorFacts()
    {
        var context = new UpgradeContext(Guid.NewGuid());
        context.RecordFact("repository-analyzer", "repository-map", new { Projects = 3 });
        context.RecordFact("version-detector", "version-manifest", new { DotNet = "net10.0" });

        var agent = new DocumentationGeneratorAgent();

        var result = await agent.ExecuteAsync("Sample Upgrade Report", context);

        Assert.Contains("Sample Upgrade Report", result.Output);
        Assert.Contains("repository-map", result.Output);
        Assert.Contains("version-manifest", result.Output);
        Assert.Equal(2, result.Citations.Count);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsItsOwnReportAsFact()
    {
        var context = new UpgradeContext(Guid.NewGuid());
        var agent = new DocumentationGeneratorAgent();

        await agent.ExecuteAsync("Report", context);

        Assert.NotNull(context.LatestFact("upgrade-report"));
    }

    [Fact]
    public async Task ValidateAsync_Succeeds_WhenReportCitesAllPriorFacts()
    {
        var context = new UpgradeContext(Guid.NewGuid());
        context.RecordFact("repository-analyzer", "repository-map", new { });

        var agent = new DocumentationGeneratorAgent();
        var result = await agent.ExecuteAsync("Report", context);

        var validation = await agent.ValidateAsync(result.Output, context);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenReportMissingAFactCitation()
    {
        var context = new UpgradeContext(Guid.NewGuid());
        context.RecordFact("repository-analyzer", "repository-map", new { });

        var agent = new DocumentationGeneratorAgent();

        var validation = await agent.ValidateAsync("a report that mentions nothing", context);

        Assert.False(validation.IsValid);
    }
}
