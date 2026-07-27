using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Agents.Pipeline.Validation.TestRunner;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Validation;

public class TestRunnerAgentTests
{
    private const string PassingSummary =
        "Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 47 ms - Foo.dll (net10.0)";

    private const string FailingSummary =
        "Failed!  - Failed:     2, Passed:     8, Skipped:     0, Total:    10, Duration: 47 ms - Foo.dll (net10.0)";

    [Fact]
    public async Task ExecuteAsync_ParsesPassingSummary()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, PassingSummary, ""));
        var agent = new TestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/Foo.Tests/Foo.Tests.csproj", context);

        Assert.True(result.Output.Succeeded);
        Assert.Equal(10, result.Output.Total);
        Assert.Equal(10, result.Output.Passed);
        Assert.Equal(100, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesFailingSummary()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, FailingSummary, ""));
        var agent = new TestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/Foo.Tests/Foo.Tests.csproj", context);

        Assert.False(result.Output.Succeeded);
        Assert.Equal(2, result.Output.Failed);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenAnyTestFailed()
    {
        var agent = new TestRunnerAgent(new FakeProcessRunner(new ProcessRunResult(1, "", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failing = new TestRunResult(false, 10, 8, 2, 0, "");

        var validation = await agent.ValidateAsync(failing, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealDotnetTest_PassesAgainstOwnDomainTestsProject()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "tests", "UpgradePilot.Core.Domain.Tests", "UpgradePilot.Core.Domain.Tests.csproj");

        var agent = new TestRunnerAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(projectPath, context);

        Assert.True(result.Output.Succeeded, result.Output.RawOutput);
        Assert.True(result.Output.Total > 0);
    }
}
