using System.Text.RegularExpressions;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Validation.TestRunner;

/// <summary>
/// The React/Next.js counterpart to <see cref="TestRunnerAgent"/> - runs the project's
/// own `npm test` script for real. Test runners in the JS ecosystem each print their own
/// summary format; this agent recognizes the two most common defaults (Jest's
/// "Tests: X failed, Y passed, Z total" and Vitest's "Tests  X failed | Y passed (Z)")
/// and parses real counts out of them. Anything else (Playwright, Mocha, a custom
/// script, ...) falls back to exit-code pass/fail only - documented, not silently wrong.
/// </summary>
public sealed partial class FrontendTestRunnerAgent : IUpgradePilotAgent<string, FrontendTestResult>
{
    [GeneratedRegex(@"^Tests:\s*(?<body>.+)$", RegexOptions.Multiline)]
    private static partial Regex JestSummaryLineRegex();

    [GeneratedRegex(@"^\s*Tests\s+(?<body>.+?)\((?<total>\d+)\)\s*$", RegexOptions.Multiline)]
    private static partial Regex VitestSummaryLineRegex();

    private readonly IProcessRunner _processRunner;

    public FrontendTestRunnerAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "frontend-test-runner-agent";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => new(MaxAttempts: 1, InitialDelay: TimeSpan.Zero, UseExponentialBackoff: false);

    public async Task<AgentResult<FrontendTestResult>> ExecuteAsync(
        string projectDirectory, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var run = await _processRunner.RunAsync("npm", "test", projectDirectory, cancellationToken);
        var combinedOutput = run.StandardOutput + run.StandardError;
        var summary = TryParseSummary(combinedOutput);

        var testResult = new FrontendTestResult(
            run.ExitCode == 0, combinedOutput, summary?.Total, summary?.Passed, summary?.Failed, summary?.Skipped);
        context.RecordFact(AgentId, "frontend-test-result", testResult);

        var explanation = testResult switch
        {
            { Succeeded: true, Total: not null } => $"{testResult.Passed}/{testResult.Total} test(s) passed.",
            { Succeeded: true } => "Frontend test suite passed.",
            { Succeeded: false, Total: not null } => $"{testResult.Failed} of {testResult.Total} test(s) failed.",
            _ => "Frontend test suite failed."
        };

        var result = testResult.Succeeded
            ? AgentResult<FrontendTestResult>.Create(testResult, 100, explanation, citations: [new Citation("npm test")])
            : AgentResult<FrontendTestResult>.Create(testResult, 0, explanation, citations: [new Citation("npm test output")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        FrontendTestResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Succeeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("Frontend test suite did not pass."));

    private static (int Total, int Passed, int Failed, int Skipped)? TryParseSummary(string output)
    {
        var jestMatch = JestSummaryLineRegex().Match(output);
        if (jestMatch.Success)
        {
            var body = jestMatch.Groups["body"].Value;
            var passed = ExtractCount(body, "passed");
            var total = ExtractCount(body, "total");
            if (passed is not null && total is not null)
            {
                return (total.Value, passed.Value, ExtractCount(body, "failed") ?? 0, ExtractCount(body, "skipped") ?? 0);
            }
        }

        var vitestMatch = VitestSummaryLineRegex().Match(output);
        if (vitestMatch.Success)
        {
            var body = vitestMatch.Groups["body"].Value;
            var total = int.Parse(vitestMatch.Groups["total"].Value);
            var failed = ExtractCount(body, "failed") ?? 0;
            var skipped = ExtractCount(body, "skipped") ?? 0;
            var passed = ExtractCount(body, "passed") ?? (total - failed - skipped);
            return (total, passed, failed, skipped);
        }

        return null;
    }

    private static int? ExtractCount(string text, string label)
    {
        var match = Regex.Match(text, $@"(?<count>\d+)\s+{label}");
        return match.Success ? int.Parse(match.Groups["count"].Value) : null;
    }
}
