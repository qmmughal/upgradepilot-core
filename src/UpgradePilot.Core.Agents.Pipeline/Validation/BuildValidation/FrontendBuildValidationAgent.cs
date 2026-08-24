using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;

/// <summary>
/// The React/Next.js counterpart to <see cref="BuildValidationAgent"/> - runs the
/// project's own `npm run build` for real (backs the `build.compileFrontend` tool named
/// in docs/architecture/agents.md §4.15 but never implemented before this). Unlike
/// dotnet's fixed CLI diagnostic format, frontend build tooling (webpack/vite/Next.js/
/// CRA) has no single stable error format to regex-parse, so this agent reports
/// pass/fail plus raw output rather than structured diagnostics - a documented scope
/// limit, not a fabricated capability.
/// </summary>
public sealed class FrontendBuildValidationAgent : IUpgradePilotAgent<string, FrontendBuildResult>
{
    private readonly IProcessRunner _processRunner;

    public FrontendBuildValidationAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "frontend-build-validation-agent";
    public string Version => "0.1.0";

    /// <summary>Build is deterministic; retries can't change the outcome, matching the dotnet build agent's policy.</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public async Task<AgentResult<FrontendBuildResult>> ExecuteAsync(
        string projectDirectory, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var run = await _processRunner.RunAsync("npm", "run build", projectDirectory, cancellationToken);

        var buildResult = new FrontendBuildResult(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        context.RecordFact(AgentId, "frontend-build-result", buildResult);

        var result = buildResult.Succeeded
            ? AgentResult<FrontendBuildResult>.Create(
                buildResult, 100, "Frontend build succeeded.", citations: [new Citation("npm run build")])
            : AgentResult<FrontendBuildResult>.Create(
                buildResult, 0, "Frontend build failed.", citations: [new Citation("npm run build output")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        FrontendBuildResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Succeeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("Frontend build did not succeed."));
}
