using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

/// <summary>
/// Next.js's counterpart to <see cref="ReactCodemodAgent"/> - same rationale: delegate
/// to Vercel's own maintained `@next/codemod` CLI (via `npx`) rather than hand-roll
/// Pages-Router-to-App-Router or server/client-boundary rewrites. Which transform names
/// to run is decided by the caller (ideally driven by <see cref="Discovery.FrameworkDetector.NextJsRoutingMode"/> -
/// e.g. only offer the Pages-to-App migration transforms when the repo is still on
/// Pages Router) - this agent's job is running them and reporting outcomes.
/// </summary>
public sealed class NextJsCodemodAgent : IUpgradePilotAgent<NextJsCodemodInput, NextJsCodemodReport>
{
    private readonly IProcessRunner _processRunner;

    public NextJsCodemodAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "nextjs-codemod-agent";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public async Task<AgentResult<NextJsCodemodReport>> ExecuteAsync(
        NextJsCodemodInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var runs = new List<NextJsCodemodRunResult>();

        foreach (var transform in input.Transforms)
        {
            var run = await _processRunner.RunAsync(
                "npx", $"@next/codemod {transform} \"{input.ProjectPath}\"", input.ProjectPath, cancellationToken);

            runs.Add(new NextJsCodemodRunResult(transform, run.ExitCode == 0, run.StandardOutput + run.StandardError));
        }

        var report = new NextJsCodemodReport(runs);
        context.RecordFact(AgentId, "nextjs-codemod-report", report);

        var failedCount = runs.Count(r => !r.Succeeded);
        var result = failedCount == 0
            ? AgentResult<NextJsCodemodReport>.Create(
                report, runs.Count == 0 ? 100 : 80, $"Applied {runs.Count} @next/codemod transform(s) successfully.",
                citations: runs.Select(r => new Citation($"@next/codemod: {r.Transform}")).ToList())
            : AgentResult<NextJsCodemodReport>.Create(
                report, 30, $"{failedCount} of {runs.Count} @next/codemod transform(s) failed.",
                citations: runs.Select(r => new Citation($"@next/codemod: {r.Transform}")).ToList());

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        NextJsCodemodReport output, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var failed = output.Runs.Where(r => !r.Succeeded).Select(r => $"Transform '{r.Transform}' failed.").ToArray();
        return Task.FromResult(failed.Length == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(failed));
    }
}
