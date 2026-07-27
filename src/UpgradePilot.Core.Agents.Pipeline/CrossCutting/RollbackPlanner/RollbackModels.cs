namespace UpgradePilot.Core.Agents.Pipeline.CrossCutting.RollbackPlanner;

public sealed record RollbackSnapshot(string RepositoryPath, string SnapshotRef, DateTimeOffset CreatedAt);

public sealed record RollbackReport(bool Succeeded, string Message);

public abstract record RollbackPlannerRequest;

public sealed record CreateSnapshotRequest(string RepositoryPath) : RollbackPlannerRequest;

public sealed record ExecuteRollbackRequest(string RepositoryPath, string SnapshotRef) : RollbackPlannerRequest;

public abstract record RollbackPlannerResponse;

public sealed record SnapshotCreated(RollbackSnapshot Snapshot) : RollbackPlannerResponse;

public sealed record RollbackExecuted(RollbackReport Report) : RollbackPlannerResponse;
