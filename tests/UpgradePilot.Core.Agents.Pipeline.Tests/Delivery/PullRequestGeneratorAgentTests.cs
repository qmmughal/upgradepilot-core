using UpgradePilot.Core.Agents.Pipeline.Delivery.PullRequestGenerator;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Delivery;

public class PullRequestGeneratorAgentTests
{
    private static PullRequestInput Input(bool securityBlocks = false) => new(
        RepositoryPath: "/repo",
        BranchName: "upgradepilot/upgrade-session-123",
        BaseBranch: "main",
        Title: "Upgrade to net10.0",
        Body: "## Upgrade report\n\nDetails here.",
        SecurityBlocksProgression: securityBlocks);

    [Fact]
    public async Task ExecuteAsync_RefusesToRun_WhenSecurityBlocksProgression()
    {
        var runner = new SequencedProcessRunner();
        var agent = new PullRequestGeneratorAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(Input(securityBlocks: true), context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.Url);
        Assert.Empty(runner.Calls); // never even checks for an existing PR
    }

    [Fact]
    public async Task ExecuteAsync_ReusesExistingPr_WithoutPushingOrCreating()
    {
        var runner = new SequencedProcessRunner(new ProcessRunResult(0, "https://github.com/example/repo/pull/42\n", ""));
        var agent = new PullRequestGeneratorAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(Input(), context);

        Assert.True(result.Output.WasAlreadyOpen);
        Assert.Equal("https://github.com/example/repo/pull/42", result.Output.Url);
        Assert.Single(runner.Calls);
        Assert.Contains("pr list", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_PushesThenCreates_WhenNoExistingPr()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "", ""), // pr list -> empty, no existing PR
            new ProcessRunResult(0, "", ""), // git push
            new ProcessRunResult(0, "https://github.com/example/repo/pull/99\n", "")); // gh pr create
        var agent = new PullRequestGeneratorAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(Input(), context);

        Assert.False(result.Output.WasAlreadyOpen);
        Assert.Equal("https://github.com/example/repo/pull/99", result.Output.Url);
        Assert.Equal(90, result.Confidence);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Contains("push", runner.Calls[1].Arguments);
        Assert.Contains("pr create", runner.Calls[2].Arguments);
        Assert.NotNull(context.LatestFact("pull-request-record"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenGitPushFails()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "", ""),
            new ProcessRunResult(1, "", "fatal: could not push"));
        var agent = new PullRequestGeneratorAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(Input(), context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.Url);
        Assert.Equal(2, runner.Calls.Count); // never attempts gh pr create
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenGhPrCreateFails()
    {
        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "", ""),
            new ProcessRunResult(0, "", ""),
            new ProcessRunResult(1, "", "HTTP 422: Validation Failed"));
        var agent = new PullRequestGeneratorAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(Input(), context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.Url);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenNoUrlRecorded()
    {
        var agent = new PullRequestGeneratorAgent(new SequencedProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var validation = await agent.ValidateAsync(new PullRequestRecord("", false), context);

        Assert.False(validation.IsValid);
    }
}
