using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Agents.Pipeline.Validation.TestRunner;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Validation;

public class FrontendTestRunnerAgentTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsSuccess_WhenNpmTestExitsZero()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "5 passed", ""));
        var agent = new FrontendTestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.True(result.Output.Succeeded);
        Assert.Equal(100, result.Confidence);
        Assert.Equal("npm", runner.LastFileName);
        Assert.Equal("test", runner.LastArguments);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFailure_WhenNpmTestExitsNonZero()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, "", "1 failed"));
        var agent = new FrontendTestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.False(result.Output.Succeeded);

        var validation = await agent.ValidateAsync(result.Output, context);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesJestSummaryLine()
    {
        var output = """
            Test Suites: 1 passed, 1 total
            Tests:       2 failed, 1 skipped, 12 passed, 15 total
            Snapshots:   0 total
            Time:        1.234 s
            """;
        var runner = new FakeProcessRunner(new ProcessRunResult(0, output, ""));
        var agent = new FrontendTestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.Equal(15, result.Output.Total);
        Assert.Equal(12, result.Output.Passed);
        Assert.Equal(2, result.Output.Failed);
        Assert.Equal(1, result.Output.Skipped);
        Assert.Contains("12/15", result.Explanation);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesJestSummaryLine_WithNoFailuresOrSkips()
    {
        var output = "Tests:       12 passed, 12 total";
        var runner = new FakeProcessRunner(new ProcessRunResult(0, output, ""));
        var agent = new FrontendTestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.Equal(12, result.Output.Total);
        Assert.Equal(12, result.Output.Passed);
        Assert.Equal(0, result.Output.Failed);
        Assert.Equal(0, result.Output.Skipped);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesVitestSummaryLine()
    {
        var output = """
            Test Files  1 failed | 3 passed (4)
                 Tests  2 failed | 12 passed | 1 skipped (15)
              Duration  1.23s
            """;
        var runner = new FakeProcessRunner(new ProcessRunResult(1, output, ""));
        var agent = new FrontendTestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.Equal(15, result.Output.Total);
        Assert.Equal(12, result.Output.Passed);
        Assert.Equal(2, result.Output.Failed);
        Assert.Equal(1, result.Output.Skipped);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesCountsNull_ForUnrecognizedRunnerOutput()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ok - 5 assertions passed", ""));
        var agent = new FrontendTestRunnerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/frontend", context);

        Assert.Null(result.Output.Total);
        Assert.True(result.Output.Succeeded);
        Assert.Equal("Frontend test suite passed.", result.Explanation);
    }
}
