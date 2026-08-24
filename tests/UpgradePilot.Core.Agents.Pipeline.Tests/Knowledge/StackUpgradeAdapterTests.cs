using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class StackUpgradeAdapterTests
{
    private static IProcessRunner NoopRunner => new FakeProcessRunner(new ProcessRunResult(0, "", ""));

    [Fact]
    public async Task DotNetUpgradeAdapter_BuildsDotNetSpecificPlan()
    {
        var adapter = new DotNetStackUpgradeAdapter(NoopRunner);
        var profile = new FrameworkProfile([], false, StackKind.DotNet);
        var repo = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(StackKind.DotNet, plan.Kind);
        Assert.Contains("upgrade-nuget-packages", plan.Steps);
        Assert.Contains("apply-ef-core-migration-planning", plan.Steps);
        Assert.Equal("/repo/Sample.Web/Sample.Web.csproj", plan.ProjectPath);
    }

    [Fact]
    public async Task ReactUpgradeAdapter_BuildsReactSpecificPlan()
    {
        var adapter = new ReactStackUpgradeAdapter(NoopRunner);
        var profile = new FrameworkProfile([], false, StackKind.React);
        var repo = new RepositoryMap("/repo", [new ProjectInfo("frontend", "/repo/package.json", [])]);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(StackKind.React, plan.Kind);
        Assert.Contains("upgrade-react-and-dom", plan.Steps);
        Assert.Contains("update-build-config", plan.Steps);
        Assert.Equal("/repo/package.json", plan.ProjectPath);
    }

    [Fact]
    public async Task NextJsUpgradeAdapter_BuildsNextJsSpecificPlan()
    {
        var adapter = new NextJsStackUpgradeAdapter(NoopRunner);
        var profile = new FrameworkProfile([], false, StackKind.NextJs);
        var repo = new RepositoryMap("/repo", [new ProjectInfo("webapp", "/repo/package.json", [])]);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(StackKind.NextJs, plan.Kind);
        Assert.Contains("migrate-app-router-or-pages-router", plan.Steps);
        Assert.Contains("update-images-and-config", plan.Steps);
        Assert.Equal("/repo/package.json", plan.ProjectPath);
    }

    [Fact]
    public async Task MixedUpgradeAdapter_BuildsMixedSpecificPlan()
    {
        var adapter = new MixedStackUpgradeAdapter(NoopRunner);
        var profile = new FrameworkProfile([], false, StackKind.Mixed);
        var repo = new RepositoryMap("/repo", [
            new ProjectInfo("backend", "/repo/backend/backend.csproj", []),
            new ProjectInfo("frontend", "/repo/package.json", [])
        ]);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(StackKind.Mixed, plan.Kind);
        Assert.Contains("coordinate-migration-order", plan.Steps);
    }

    [Fact]
    public async Task Coordinator_ResolvesDotNetAdapter_ForDotNetRepos()
    {
        var coordinator = new StackUpgradeCoordinator(
            new StackUpgradeRegistry([
                new DotNetStackUpgradeAdapter(NoopRunner),
                new ReactStackUpgradeAdapter(NoopRunner),
                new NextJsStackUpgradeAdapter(NoopRunner),
                new MixedStackUpgradeAdapter(NoopRunner)]));

        var repo = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);
        var profile = new FrameworkProfile([], false, StackKind.DotNet);

        var adapter = coordinator.Resolve(profile);
        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(StackKind.DotNet, plan.Kind);
        Assert.Contains("upgrade-nuget-packages", plan.Steps);
    }

    [Fact]
    public async Task Coordinator_ResolvesMixedAdapter_ForMixedRepos()
    {
        var coordinator = new StackUpgradeCoordinator(
            new StackUpgradeRegistry([
                new DotNetStackUpgradeAdapter(NoopRunner),
                new ReactStackUpgradeAdapter(NoopRunner),
                new NextJsStackUpgradeAdapter(NoopRunner),
                new MixedStackUpgradeAdapter(NoopRunner)]));

        var repo = new RepositoryMap("/repo", [
            new ProjectInfo("backend", "/repo/backend/backend.csproj", []),
            new ProjectInfo("frontend", "/repo/package.json", [])
        ]);
        var profile = new FrameworkProfile([], false, StackKind.Mixed);

        var adapter = coordinator.Resolve(profile);
        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(StackKind.Mixed, plan.Kind);
        Assert.Contains("coordinate-migration-order", plan.Steps);
    }

    [Fact]
    public async Task Plan_StepsComeFromStrategyCatalog_SoTheyCannotDriftFromUpgradePlanner()
    {
        var adapter = new ReactStackUpgradeAdapter(NoopRunner);
        var profile = new FrameworkProfile([], false, StackKind.React);
        var repo = new RepositoryMap("/repo", [new ProjectInfo("frontend", "/repo/package.json", [])]);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        var strategy = UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner.StackUpgradeStrategyCatalog.Resolve(StackKind.React);
        Assert.Equal(strategy.ExecutionSteps, plan.Steps);
    }
}
