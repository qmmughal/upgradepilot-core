using System.Text.RegularExpressions;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Validation.TestRunner;

/// <summary>
/// Agent #16 (docs/architecture/agents.md §4.16): runs the test suite for the given
/// test project via `dotnet test` and parses the trx-free CLI summary line
/// ("Failed: N, Passed: N, Skipped: N, Total: N"). A failing test always counts as a
/// regression signal per the spec - this v0.1 has no flaky-test isolation retry yet
/// (spec allows exactly one, for flake detection).
/// </summary>
public sealed partial class TestRunnerAgent : IUpgradePilotAgent<string, TestRunResult>
{
    [GeneratedRegex(@"Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)")]
    private static partial Regex SummaryRegex();

    private readonly IProcessRunner _processRunner;

    public TestRunnerAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "test-runner-agent";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => new(MaxAttempts: 1, InitialDelay: TimeSpan.Zero, UseExponentialBackoff: false);

    public async Task<AgentResult<TestRunResult>> ExecuteAsync(
        string testProjectPath, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var workingDirectory = Path.GetDirectoryName(testProjectPath) ?? Directory.GetCurrentDirectory();

        var run = await _processRunner.RunAsync(
            "dotnet", $"test \"{testProjectPath}\" --nologo", workingDirectory, cancellationToken);

        var testResult = ParseSummary(run.StandardOutput, run.ExitCode == 0);
        context.RecordFact(AgentId, "test-result", testResult);

        var result = testResult.Succeeded
            ? AgentResult<TestRunResult>.Create(
                testResult, 100, $"{testResult.Passed}/{testResult.Total} test(s) passed.",
                citations: [new Citation("dotnet test")])
            : AgentResult<TestRunResult>.Create(
                testResult, 0, $"{testResult.Failed} of {testResult.Total} test(s) failed.",
                citations: [new Citation("dotnet test output")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        TestRunResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Succeeded
            ? ValidationResult.Success()
            : ValidationResult.Failure($"{output.Failed} test(s) failed."));

    private static TestRunResult ParseSummary(string output, bool processExitedZero)
    {
        var match = SummaryRegex().Match(output);
        if (!match.Success)
        {
            return new TestRunResult(processExitedZero, 0, 0, 0, 0, output);
        }

        var failed = int.Parse(match.Groups["failed"].Value);
        var passed = int.Parse(match.Groups["passed"].Value);
        var skipped = int.Parse(match.Groups["skipped"].Value);
        var total = int.Parse(match.Groups["total"].Value);

        return new TestRunResult(failed == 0, total, passed, failed, skipped, output);
    }
}
