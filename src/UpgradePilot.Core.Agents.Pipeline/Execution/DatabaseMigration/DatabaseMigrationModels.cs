namespace UpgradePilot.Core.Agents.Pipeline.Execution.DatabaseMigration;

public sealed record MigrationInput(string ProjectPath, string MigrationName, string? StartupProjectPath = null);

public sealed record DestructiveOperation(string OperationType, string Description);

public sealed record MigrationSafetyReport(
    string MigrationName,
    bool Succeeded,
    IReadOnlyList<DestructiveOperation> DestructiveOperations,
    string RawOutput);
