using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Domain.Tests.Agents;

/// <summary>
/// A minimal fake agent proving IUpgradePilotAgent is implementable end-to-end: it reads
/// no prior facts, writes one, and returns a result with an explanation and citation.
/// </summary>
file sealed class EchoAgent : IUpgradePilotAgent<string, string>
{
    public string AgentId => "echo-agent";
    public string Version => "0.1.0";
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<string>> ExecuteAsync(
        string input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        context.RecordFact(AgentId, "echo-output", input);

        var result = AgentResult<string>.Create(
            output: input,
            confidence: 100,
            explanation: $"Echoed input verbatim: '{input}'.",
            citations: [new Citation("EchoAgent self-test")]);

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        string output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.IsNullOrEmpty(output)
            ? ValidationResult.Failure("Output must not be empty.")
            : ValidationResult.Success());
}

public class UpgradePilotAgentContractTests
{
    [Fact]
    public async Task ExecuteAsync_RecordsFact_AndReturnsExplainableResult()
    {
        var agent = new EchoAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("hello", context);

        Assert.Equal("hello", result.Output);
        Assert.Equal(100, result.Confidence);
        Assert.NotEmpty(result.Explanation);
        Assert.NotEmpty(result.Citations);
        Assert.Single(context.FactsFrom("echo-agent"));
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenOutputEmpty()
    {
        var agent = new EchoAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var validation = await agent.ValidateAsync(string.Empty, context);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }
}
