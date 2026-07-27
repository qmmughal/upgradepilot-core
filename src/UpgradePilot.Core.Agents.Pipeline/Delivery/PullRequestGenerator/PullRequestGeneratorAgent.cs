using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Delivery.PullRequestGenerator;

/// <summary>
/// Agent #19 (docs/architecture/agents.md §4.19): pushes the worktree branch and opens
/// a PR with the upgrade report embedded. Real `git push` + `gh pr create` (both
/// already proven to work against a real GitHub repo earlier in this project's
/// history) via IProcessRunner. Idempotent per spec: checks `gh pr list --head` first
/// so re-running the pipeline doesn't open a duplicate PR. Hard policy gate: refuses
/// to run at all if Security Validation Agent reported a blocking finding, regardless
/// of what the caller passes as branch/title/body.
/// </summary>
public sealed class PullRequestGeneratorAgent : IUpgradePilotAgent<PullRequestInput, PullRequestRecord>
{
    private readonly IProcessRunner _processRunner;

    public PullRequestGeneratorAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "pull-request-generator";
    public string Version => "0.1.0";

    /// <summary>3 attempts on API rate limit/network error, per spec §4.19.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: true);

    public async Task<AgentResult<PullRequestRecord>> ExecuteAsync(
        PullRequestInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        if (input.SecurityBlocksProgression)
        {
            var blocked = new PullRequestRecord(string.Empty, false);
            return AgentResult<PullRequestRecord>.Create(
                blocked, 0, "PR not opened: Security Validation Agent reported an unresolved Critical/High finding.");
        }

        var existingUrl = await FindExistingPrAsync(input, cancellationToken);
        if (existingUrl is not null)
        {
            var existing = new PullRequestRecord(existingUrl, WasAlreadyOpen: true);
            context.RecordFact(AgentId, "pull-request-record", existing);
            return AgentResult<PullRequestRecord>.Create(
                existing, 100, "PR already exists for this branch - reused instead of creating a duplicate.",
                citations: [new Citation("gh pr list")]);
        }

        var pushRun = await _processRunner.RunAsync(
            "git", $"push -u origin \"{input.BranchName}\"", input.RepositoryPath, cancellationToken);
        if (pushRun.ExitCode != 0)
        {
            var failed = new PullRequestRecord(string.Empty, false);
            return AgentResult<PullRequestRecord>.Create(failed, 0, $"git push failed: {pushRun.StandardError.Trim()}");
        }

        var bodyFile = Path.Combine(Path.GetTempPath(), $"upgradepilot-pr-body-{Guid.NewGuid()}.md");
        try
        {
            await File.WriteAllTextAsync(bodyFile, input.Body, cancellationToken);

            var createRun = await _processRunner.RunAsync(
                "gh",
                $"pr create --title \"{input.Title}\" --body-file \"{bodyFile}\" --base \"{input.BaseBranch}\" --head \"{input.BranchName}\"",
                input.RepositoryPath, cancellationToken);

            if (createRun.ExitCode != 0)
            {
                var failed = new PullRequestRecord(string.Empty, false);
                return AgentResult<PullRequestRecord>.Create(failed, 0, $"gh pr create failed: {createRun.StandardError.Trim()}");
            }

            var url = createRun.StandardOutput.Trim();
            var record = new PullRequestRecord(url, WasAlreadyOpen: false);
            context.RecordFact(AgentId, "pull-request-record", record);

            return AgentResult<PullRequestRecord>.Create(
                record, 90, $"Opened PR: {url}", citations: [new Citation("gh pr create")]);
        }
        finally
        {
            if (File.Exists(bodyFile))
            {
                File.Delete(bodyFile);
            }
        }
    }

    public Task<ValidationResult> ValidateAsync(
        PullRequestRecord output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrEmpty(output.Url)
            ? ValidationResult.Success()
            : ValidationResult.Failure("No PR URL recorded."));

    private async Task<string?> FindExistingPrAsync(PullRequestInput input, CancellationToken cancellationToken)
    {
        var run = await _processRunner.RunAsync(
            "gh", $"pr list --head \"{input.BranchName}\" --json url --jq \".[0].url\"",
            input.RepositoryPath, cancellationToken);

        var trimmed = run.StandardOutput.Trim();
        return run.ExitCode == 0 && trimmed.Length > 0 ? trimmed : null;
    }
}
