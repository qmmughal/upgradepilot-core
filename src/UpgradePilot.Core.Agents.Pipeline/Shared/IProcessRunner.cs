namespace UpgradePilot.Core.Agents.Pipeline.Shared;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// How pipeline agents invoke external tooling (dotnet build/test, git). In-process
/// stand-in for the `build.*`/`test.*`/`git.*` MCP tools described in
/// docs/architecture/agents.md — same role as IRepositoryReader plays for `repo.*`.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default);
}
