using UpgradePilot.Core.Agents.Pipeline.CrossCutting.RollbackPlanner;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.CrossCutting;

public class RollbackPlannerAgentTests
{
    [Fact]
    public async Task ExecuteAsync_CreateSnapshot_RecordsFact_AndReturnsSnapshotCreated()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "", ""));
        var agent = new RollbackPlannerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new CreateSnapshotRequest("/repo"), context);

        var response = Assert.IsType<SnapshotCreated>(result.Output);
        Assert.StartsWith("upgradepilot-pre-image-", response.Snapshot.SnapshotRef);
        Assert.NotNull(context.LatestFact("rollback-snapshot"));
        Assert.Equal(100, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ExecuteRollback_ReturnsFailure_WhenGitResetFails()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, "", "fatal: bad revision"));
        var agent = new RollbackPlannerAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new ExecuteRollbackRequest("/repo", "bad-ref"), context);

        var response = Assert.IsType<RollbackExecuted>(result.Output);
        Assert.False(response.Report.Succeeded);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenRollbackDidNotSucceed()
    {
        var agent = new RollbackPlannerAgent(new FakeProcessRunner(new ProcessRunResult(0, "", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failed = new RollbackExecuted(new RollbackReport(false, "fatal: bad revision"));

        var validation = await agent.ValidateAsync(failed, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task RealGit_SnapshotThenRollback_RestoresFileContent()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), "upgradepilot-rollback-test-" + Guid.NewGuid());
        Directory.CreateDirectory(repoPath);

        try
        {
            var git = new SystemProcessRunner();
            await git.RunAsync("git", "init", repoPath);
            await git.RunAsync("git", "config user.email \"test@upgradepilot.dev\"", repoPath);
            await git.RunAsync("git", "config user.name \"UpgradePilot Test\"", repoPath);

            var filePath = Path.Combine(repoPath, "file.txt");
            await File.WriteAllTextAsync(filePath, "original content");
            await git.RunAsync("git", "add .", repoPath);
            await git.RunAsync("git", "commit -m \"initial\"", repoPath);

            var agent = new RollbackPlannerAgent(git);
            var context = new UpgradeContext(Guid.NewGuid());

            var snapshotResult = await agent.ExecuteAsync(new CreateSnapshotRequest(repoPath), context);
            var snapshot = ((SnapshotCreated)snapshotResult.Output).Snapshot;

            await File.WriteAllTextAsync(filePath, "modified content - should be rolled back");

            var rollbackResult = await agent.ExecuteAsync(
                new ExecuteRollbackRequest(repoPath, snapshot.SnapshotRef), context);

            Assert.True(((RollbackExecuted)rollbackResult.Output).Report.Succeeded);
            Assert.Equal("original content", await File.ReadAllTextAsync(filePath));
        }
        finally
        {
            DeleteDirectoryForcefully(repoPath);
        }
    }

    /// <summary>
    /// Windows marks some files under .git (e.g. pack files) read-only, which makes
    /// Directory.Delete(recursive: true) throw UnauthorizedAccessException. Clear the
    /// attribute on every file first.
    /// </summary>
    private static void DeleteDirectoryForcefully(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
