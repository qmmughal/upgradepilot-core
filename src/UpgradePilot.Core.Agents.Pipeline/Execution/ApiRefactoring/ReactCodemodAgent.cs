using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

/// <summary>
/// React's counterpart to <see cref="ApiRefactoringAgent"/>, but for a different reason
/// than "not built yet": for JS/TS source, the right move is to delegate to the
/// official, maintained `react-codemod` CLI (via `npx`) rather than build a bespoke
/// JS/TS AST rewriter in C# - the codebase already has no JS/TS parsing capability, and
/// re-implementing transforms Facebook/the community already maintain (legacy
/// ReactDOM.render -> createRoot, PropTypes/defaultProps deprecations, ...) would be
/// duplicated, lower-quality work. Each requested transform name is passed straight
/// through to the CLI - this agent's job is orchestration and reporting, not knowing
/// the specific breaking-change catalog (that's Release Notes Intelligence's job, once
/// wired - see the scope note on <see cref="Execution.StackAdapters.StackUpgradePlan"/>).
/// </summary>
public sealed class ReactCodemodAgent : IUpgradePilotAgent<ReactCodemodInput, ReactCodemodReport>
{
    private readonly IProcessRunner _processRunner;

    public ReactCodemodAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "react-codemod-agent";
    public string Version => "0.1.0";

    /// <summary>Not retried - a codemod either applies or it doesn't; the loop belongs to the caller sequencing transforms, not this agent.</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public async Task<AgentResult<ReactCodemodReport>> ExecuteAsync(
        ReactCodemodInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var runs = new List<ReactCodemodRunResult>();

        foreach (var transform in input.Transforms)
        {
            var run = await _processRunner.RunAsync(
                "npx", $"react-codemod {transform} \"{input.ProjectPath}\"", input.ProjectPath, cancellationToken);

            runs.Add(new ReactCodemodRunResult(transform, run.ExitCode == 0, run.StandardOutput + run.StandardError));
        }

        var report = new ReactCodemodReport(runs);
        context.RecordFact(AgentId, "react-codemod-report", report);

        var failedCount = runs.Count(r => !r.Succeeded);
        var result = failedCount == 0
            ? AgentResult<ReactCodemodReport>.Create(
                report, runs.Count == 0 ? 100 : 80, $"Applied {runs.Count} react-codemod transform(s) successfully.",
                citations: runs.Select(r => new Citation($"react-codemod: {r.Transform}")).ToList())
            : AgentResult<ReactCodemodReport>.Create(
                report, 30, $"{failedCount} of {runs.Count} react-codemod transform(s) failed.",
                citations: runs.Select(r => new Citation($"react-codemod: {r.Transform}")).ToList());

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        ReactCodemodReport output, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var failed = output.Runs.Where(r => !r.Succeeded).Select(r => $"Transform '{r.Transform}' failed.").ToArray();
        return Task.FromResult(failed.Length == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(failed));
    }
}
