using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.DatabaseMigration;

/// <summary>
/// Agent #14 (docs/architecture/agents.md §4.14): brings the database schema forward.
/// Real `dotnet ef migrations add` (via IProcessRunner) against the target project's
/// DbContext - EF Core always generates the down-migration alongside the up-migration
/// automatically, which is what satisfies the spec's "every migration must have a
/// corresponding down-migration" rule. This agent adds one more check on top: scanning
/// the generated migration for destructive operations (DropTable/DropColumn/etc.) and
/// flagging them rather than applying them silently.
/// </summary>
public sealed class DatabaseMigrationAgent : IUpgradePilotAgent<MigrationInput, MigrationSafetyReport>
{
    private static readonly string[] DestructiveMarkers =
        ["DropTable", "DropColumn", "DropForeignKey", "DropIndex", "DropPrimaryKey", "DropUniqueConstraint"];

    private readonly IProcessRunner _processRunner;

    public DatabaseMigrationAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "database-migration-agent";
    public string Version => "0.1.0";

    /// <summary>No retry on generation failure - a schema conflict is a planning concern, per spec §4.14.</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public async Task<AgentResult<MigrationSafetyReport>> ExecuteAsync(
        MigrationInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var projectDirectory = Path.GetDirectoryName(input.ProjectPath) ?? Directory.GetCurrentDirectory();
        var startupArg = input.StartupProjectPath is not null
            ? $" --startup-project \"{input.StartupProjectPath}\""
            : string.Empty;

        // `dotnet ef` inspects the compiled assembly for DbContext types - it needs a
        // restored/built project, unlike `dotnet build` which restores implicitly.
        await _processRunner.RunAsync("dotnet", $"restore \"{input.ProjectPath}\"", projectDirectory, cancellationToken);

        var run = await _processRunner.RunAsync(
            "dotnet", $"ef migrations add {input.MigrationName} --project \"{input.ProjectPath}\"{startupArg}",
            projectDirectory, cancellationToken);

        var rawOutput = run.StandardOutput + run.StandardError;

        if (run.ExitCode != 0)
        {
            var failedReport = new MigrationSafetyReport(input.MigrationName, false, [], rawOutput);
            context.RecordFact(AgentId, "migration-safety-report", failedReport);
            return AgentResult<MigrationSafetyReport>.Create(
                failedReport, 0, $"Migration generation failed: {run.StandardError.Trim()}");
        }

        var migrationFile = FindGeneratedMigrationFile(projectDirectory, input.MigrationName);
        var destructiveOps = migrationFile is not null
            ? ScanForDestructiveOperations(File.ReadAllText(migrationFile))
            : [];

        var report = new MigrationSafetyReport(input.MigrationName, true, destructiveOps, rawOutput);
        context.RecordFact(AgentId, "migration-safety-report", report);

        var result = destructiveOps.Count == 0
            ? AgentResult<MigrationSafetyReport>.Create(
                report, 90, "Migration generated with no destructive operations detected.",
                citations: [new Citation("dotnet ef migrations add")])
            : AgentResult<MigrationSafetyReport>.Create(
                report, 50, $"Migration generated with {destructiveOps.Count} destructive operation(s) flagged for review.",
                citations: destructiveOps.Select(o => new Citation(o.Description)).ToList());

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        MigrationSafetyReport output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Succeeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("Migration generation did not succeed."));

    private static string? FindGeneratedMigrationFile(string projectDirectory, string migrationName)
    {
        var migrationsDir = Path.Combine(projectDirectory, "Migrations");
        return Directory.Exists(migrationsDir)
            ? Directory.EnumerateFiles(migrationsDir, $"*_{migrationName}.cs").FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Only the Up() method matters here - Down() legitimately drops what Up() just
    /// created (e.g. the very first migration's rollback), which is not destructive in
    /// the sense this check cares about.
    /// </summary>
    private static IReadOnlyList<DestructiveOperation> ScanForDestructiveOperations(string migrationSource)
    {
        var upStart = migrationSource.IndexOf("protected override void Up(", StringComparison.Ordinal);
        var downStart = migrationSource.IndexOf("protected override void Down(", StringComparison.Ordinal);

        if (upStart < 0)
        {
            return [];
        }

        var upBody = downStart > upStart
            ? migrationSource[upStart..downStart]
            : migrationSource[upStart..];

        return DestructiveMarkers
            .Where(marker => upBody.Contains($"migrationBuilder.{marker}", StringComparison.Ordinal))
            .Select(marker => new DestructiveOperation(marker, $"Migration's Up() contains a {marker} call - review before applying."))
            .ToList();
    }
}
