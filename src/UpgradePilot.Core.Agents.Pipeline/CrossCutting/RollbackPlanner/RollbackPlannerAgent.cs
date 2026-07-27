using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.CrossCutting.RollbackPlanner;

/// <summary>
/// Agent #20 (docs/architecture/agents.md §4.20): guarantees every upgrade is
/// reversible. Has two real triggers per spec - snapshot at session start, rollback on
/// failure/rejection - modeled as a request/response union rather than two agents,
/// since they share retry policy and both operate on the same git-backed mechanism.
/// Snapshotting uses a `git tag`; rollback uses `git reset --hard` to that tag. This is
/// the one agent whose own failures must never be silently swallowed (per spec), so its
/// retry policy is the most aggressive of any agent implemented so far.
/// </summary>
public sealed class RollbackPlannerAgent : IUpgradePilotAgent<RollbackPlannerRequest, RollbackPlannerResponse>
{
    private readonly IProcessRunner _processRunner;

    public RollbackPlannerAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "rollback-planner";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(1), UseExponentialBackoff: true);

    public Task<AgentResult<RollbackPlannerResponse>> ExecuteAsync(
        RollbackPlannerRequest input, UpgradeContext context, CancellationToken cancellationToken = default) =>
        input switch
        {
            CreateSnapshotRequest create => CreateSnapshotAsync(create, context, cancellationToken),
            ExecuteRollbackRequest rollback => ExecuteRollbackAsync(rollback, context, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown rollback planner request type."),
        };

    public Task<ValidationResult> ValidateAsync(
        RollbackPlannerResponse output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output switch
        {
            SnapshotCreated => ValidationResult.Success(),
            RollbackExecuted { Report.Succeeded: true } => ValidationResult.Success(),
            RollbackExecuted r => ValidationResult.Failure(r.Report.Message),
            _ => ValidationResult.Failure("Unknown rollback planner response type."),
        });

    private async Task<AgentResult<RollbackPlannerResponse>> CreateSnapshotAsync(
        CreateSnapshotRequest request, UpgradeContext context, CancellationToken cancellationToken)
    {
        var tagName = $"upgradepilot-pre-image-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var run = await _processRunner.RunAsync("git", $"tag \"{tagName}\"", request.RepositoryPath, cancellationToken);

        var snapshot = new RollbackSnapshot(request.RepositoryPath, tagName, DateTimeOffset.UtcNow);
        context.RecordFact(AgentId, "rollback-snapshot", snapshot);

        var response = new SnapshotCreated(snapshot);

        return run.ExitCode == 0
            ? AgentResult<RollbackPlannerResponse>.Create(
                response, 100, $"Created pre-image tag '{tagName}'.", citations: [new Citation("git tag")])
            : AgentResult<RollbackPlannerResponse>.Create(
                response, 0, $"Failed to create pre-image tag: {run.StandardError}");
    }

    private async Task<AgentResult<RollbackPlannerResponse>> ExecuteRollbackAsync(
        ExecuteRollbackRequest request, UpgradeContext context, CancellationToken cancellationToken)
    {
        var run = await _processRunner.RunAsync(
            "git", $"reset --hard \"{request.SnapshotRef}\"", request.RepositoryPath, cancellationToken);

        var report = new RollbackReport(
            run.ExitCode == 0, run.ExitCode == 0 ? "Rollback succeeded." : run.StandardError);
        context.RecordFact(AgentId, "rollback-report", report);

        var response = new RollbackExecuted(report);

        return report.Succeeded
            ? AgentResult<RollbackPlannerResponse>.Create(
                response, 100, "Repository reset to pre-image snapshot.", citations: [new Citation("git reset --hard")])
            : AgentResult<RollbackPlannerResponse>.Create(response, 0, $"Rollback failed: {report.Message}");
    }
}
