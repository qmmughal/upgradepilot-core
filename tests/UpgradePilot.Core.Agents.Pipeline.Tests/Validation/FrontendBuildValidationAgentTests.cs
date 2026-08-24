using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Validation;

public class FrontendBuildValidationAgentTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsSuccess_WhenNpmRunBuildExitsZero()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "build output", ""));
        var agent = new FrontendBuildValidationAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.True(result.Output.Succeeded);
        Assert.Equal(100, result.Confidence);
        Assert.Equal("npm", runner.LastFileName);
        Assert.Equal("run build", runner.LastArguments);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFailure_WhenNpmRunBuildExitsNonZero()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, "", "webpack error"));
        var agent = new FrontendBuildValidationAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.False(result.Output.Succeeded);
        Assert.Equal(0, result.Confidence);

        var validation = await agent.ValidateAsync(result.Output, context);
        Assert.False(validation.IsValid);
    }
}
