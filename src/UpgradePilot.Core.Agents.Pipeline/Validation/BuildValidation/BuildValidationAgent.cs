using System.Text.RegularExpressions;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;

/// <summary>
/// Agent #15 (docs/architecture/agents.md §4.15): compiles the given project or
/// solution and structures the errors. Input is a real filesystem path to a
/// .csproj/.sln/.slnx file, compiled for real via `dotnet build` - no simulated
/// output. The regex below parses dotnet's CLI diagnostic text format, which is a
/// legitimate exception to "avoid regex" (it's parsing tool output, not transforming
/// source code).
/// </summary>
public sealed partial class BuildValidationAgent : IUpgradePilotAgent<string, BuildResult>
{
    [GeneratedRegex(@"^\s*(?<path>.+?)\((?<line>\d+),\d+\)\s*:\s*error\s+(?<code>\w+)\s*:\s*(?<message>.+?)\s*\[", RegexOptions.Multiline)]
    private static partial Regex ErrorLineRegex();

    private readonly IProcessRunner _processRunner;

    public BuildValidationAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "build-validation-agent";
    public string Version => "0.1.0";

    /// <summary>Build is deterministic; retries can't change the outcome (per spec §4.15).</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public async Task<AgentResult<BuildResult>> ExecuteAsync(
        string projectOrSolutionPath, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var workingDirectory = Path.GetDirectoryName(projectOrSolutionPath) ?? Directory.GetCurrentDirectory();

        var run = await _processRunner.RunAsync(
            "dotnet", $"build \"{projectOrSolutionPath}\" --nologo", workingDirectory, cancellationToken);

        var combinedOutput = run.StandardOutput + run.StandardError;
        var errors = ParseErrors(combinedOutput);
        var buildResult = new BuildResult(run.ExitCode == 0 && errors.Count == 0, errors, combinedOutput);

        context.RecordFact(AgentId, "build-result", buildResult);

        var result = buildResult.Succeeded
            ? AgentResult<BuildResult>.Create(
                buildResult, 100, "Build succeeded with zero compiler errors.",
                citations: [new Citation("dotnet build")])
            : AgentResult<BuildResult>.Create(
                buildResult, 0, $"Build failed with {errors.Count} error(s).",
                citations: [new Citation("dotnet build output")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        BuildResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Succeeded
            ? ValidationResult.Success()
            : ValidationResult.Failure(output.Errors.Select(e => $"{e.Code}: {e.Message}").ToArray()));

    private static IReadOnlyList<BuildDiagnostic> ParseErrors(string output) =>
        ErrorLineRegex().Matches(output)
            .Select(m => new BuildDiagnostic(
                Code: m.Groups["code"].Value,
                Message: m.Groups["message"].Value.Trim(),
                FilePath: m.Groups["path"].Value,
                Line: int.Parse(m.Groups["line"].Value)))
            .ToList();
}
