using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.VersionDetector;
using UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class UpgradePlannerAgentTests
{
    private static readonly ProjectInfo SampleProject = new("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", []);

    [Fact]
    public async Task ExecuteAsync_FullConfidence_WhenAllSignalsClean()
    {
        var input = new UpgradePlanInput(
            new RepositoryMap("/repo", [SampleProject]),
            new VersionManifest([new FrameworkVersionSignal("Abp.AspNetCore", "9.2.0", 100)]),
            new FrameworkProfile([new FrameworkClassification("Sample.Web", DetectedFramework.AbpFrameworkLegacy, 70, "matched")], false));

        var agent = new UpgradePlannerAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(input, context);

        Assert.Equal(100, result.Confidence);
        Assert.False(result.Output.RequiresHumanApproval);
        Assert.Empty(result.Output.RiskRegister);
    }

    [Fact]
    public async Task ExecuteAsync_LowersConfidence_AndRequiresApproval_WhenFrameworkUnclassified()
    {
        var input = new UpgradePlanInput(
            new RepositoryMap("/repo", [SampleProject]),
            new VersionManifest([]),
            new FrameworkProfile([new FrameworkClassification("Sample.Web", DetectedFramework.Unknown, 30, "no match")], false));

        var agent = new UpgradePlannerAgent(approvalThreshold: 85);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(input, context);

        // -20 (unclassified) - 15 (no Abp signal) = 65
        Assert.Equal(65, result.Confidence);
        Assert.True(result.Output.RequiresHumanApproval);
        Assert.Equal(2, result.Output.RiskRegister.Count);
        Assert.All(result.Output.RiskRegister, r => Assert.False(string.IsNullOrWhiteSpace(r.SourceAgentId)));
    }

    [Fact]
    public async Task ExecuteAsync_ZeroConfidence_WhenNoProjectsFound()
    {
        var input = new UpgradePlanInput(
            new RepositoryMap("/empty-repo", []),
            new VersionManifest([]),
            new FrameworkProfile([], false));

        var agent = new UpgradePlannerAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(input, context);

        Assert.Equal(0, result.Confidence);
        Assert.True(result.Output.RequiresHumanApproval);
    }

    [Fact]
    public async Task ExecuteAsync_RemainingStepsExcludeAlreadyCompletedAgents()
    {
        var input = new UpgradePlanInput(
            new RepositoryMap("/repo", [SampleProject]),
            new VersionManifest([new FrameworkVersionSignal("Abp.AspNetCore", "9.2.0", 100)]),
            new FrameworkProfile([new FrameworkClassification("Sample.Web", DetectedFramework.AbpFrameworkLegacy, 70, "matched")], false));

        var agent = new UpgradePlannerAgent();
        var context = new UpgradeContext(Guid.NewGuid());
        context.RecordFact("repository-analyzer", "repository-map", input.RepositoryMap);
        context.RecordFact("version-detector", "version-manifest", input.VersionManifest);

        var result = await agent.ExecuteAsync(input, context);

        Assert.DoesNotContain("repository-analyzer", result.Output.RemainingPipelineSteps);
        Assert.DoesNotContain("version-detector", result.Output.RemainingPipelineSteps);
        Assert.Contains("framework-detector", result.Output.RemainingPipelineSteps);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenRiskItemMissingSourceAgent()
    {
        var agent = new UpgradePlannerAgent();
        var context = new UpgradeContext(Guid.NewGuid());
        var plan = new UpgradePlan([], [new RiskItem("bad risk", "High", "")], 50, true);

        var validation = await agent.ValidateAsync(plan, context);

        Assert.False(validation.IsValid);
    }
}
