using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution.StackAdapters;

public class NextJsStackUpgradeAdapterTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-nextjs-adapter-" + Guid.NewGuid());

    public NextJsStackUpgradeAdapterTests()
    {
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task BuildUpgradePlanAsync_DerivesPackageTargetVersionsAndCodemodTransforms_FromRealDependencyAndReleaseData()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "next": "13.5.0" } }""");

        const string npmOutdatedOutput = """
            { "next": { "current": "13.5.0", "wanted": "13.5.0", "latest": "14.2.0" } }
            """;
        const string releasesOutput = """
            [
              {
                "tag_name": "v15.0.0",
                "html_url": "https://github.com/vercel/next.js/releases/tag/v15.0.0",
                "body": "* Breaking: Middleware has been renamed to proxy\n* Breaking: cookies() and headers() are now async"
              }
            ]
            """;

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(1, npmOutdatedOutput, ""),
            new ProcessRunResult(0, releasesOutput, ""));
        var adapter = new NextJsStackUpgradeAdapter(runner);
        var repo = new RepositoryMap(_fixtureDir, [new ProjectInfo("webapp", packageJsonPath, [])]);
        var profile = new FrameworkProfile([], false, StackKind.NextJs);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.NotNull(plan.PackageTargetVersions);
        Assert.Equal("14.2.0", plan.PackageTargetVersions!["next"]);
        Assert.NotNull(plan.CodemodTransforms);
        Assert.Contains("middleware-to-proxy", plan.CodemodTransforms!);
        Assert.Contains("next-async-request-api", plan.CodemodTransforms!);
    }

    [Fact]
    public async Task ApplyAsync_UpgradesPackagesAndValidatesBuild_WhenTargetVersionsSupplied()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "next": "13.5.0" } }""");

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "install ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new NextJsStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.NextJs, "Upgrade path", ["upgrade-nextjs-dependencies"], 80,
            ProjectPath: packageJsonPath,
            PackageTargetVersions: new Dictionary<string, string> { ["next"] = "14.2.0" });

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(("npm", "install"), runner.Calls[0]);
        Assert.Equal(("npm", "run build"), runner.Calls[1]);
        Assert.Contains(packageJsonPath, result.Output.ChangedFiles);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ApplyAsync_RunsCodemods_ThenValidatesBuild_WhenTransformsSupplied()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "next": "14.2.0" } }""");

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "codemod ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new NextJsStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.NextJs, "Upgrade path", ["migrate-app-router-or-pages-router"], 80,
            ProjectPath: packageJsonPath,
            CodemodTransforms: ["app-dir-runtime-config-experimental"]);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("npx", runner.Calls[0].FileName);
        Assert.Contains("app-dir-runtime-config-experimental", runner.Calls[0].Arguments);
        Assert.Equal(80, result.Confidence);
    }

    [Fact]
    public async Task ApplyAsync_DoesNothing_WhenNoPackageVersionsOrTransformsSupplied()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        var runner = new SequencedProcessRunner();
        var adapter = new NextJsStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(StackKind.NextJs, "Upgrade path", ["upgrade-nextjs-dependencies"], 80, ProjectPath: packageJsonPath);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Empty(runner.Calls);
        Assert.Equal(0, result.Confidence);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDir))
        {
            Directory.Delete(_fixtureDir, recursive: true);
        }
    }
}
