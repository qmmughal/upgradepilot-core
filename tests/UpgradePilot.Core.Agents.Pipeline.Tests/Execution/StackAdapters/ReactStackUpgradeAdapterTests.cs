using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution.StackAdapters;

public class ReactStackUpgradeAdapterTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-react-adapter-" + Guid.NewGuid());

    public ReactStackUpgradeAdapterTests()
    {
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task BuildUpgradePlanAsync_DerivesPackageTargetVersionsAndCodemodTransforms_FromRealDependencyAndReleaseData()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "react": "^17.0.0" } }""");

        const string npmOutdatedOutput = """
            { "react": { "current": "17.0.2", "wanted": "17.0.2", "latest": "18.2.0" } }
            """;
        const string releasesOutput = """
            [
              {
                "tag_name": "v19.0.0",
                "html_url": "https://github.com/facebook/react/releases/tag/v19.0.0",
                "body": "* Breaking: ReactDOM.render is removed, use createRoot instead\n* Breaking: React.PropTypes moved to a separate package"
              }
            ]
            """;

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(1, npmOutdatedOutput, ""),
            new ProcessRunResult(0, releasesOutput, ""));
        var adapter = new ReactStackUpgradeAdapter(runner);
        var repo = new RepositoryMap(_fixtureDir, [new ProjectInfo("frontend", packageJsonPath, [])]);
        var profile = new FrameworkProfile([], false, StackKind.React);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.NotNull(plan.PackageTargetVersions);
        Assert.Equal("18.2.0", plan.PackageTargetVersions!["react"]);
        Assert.NotNull(plan.CodemodTransforms);
        Assert.Contains("replace-reactdom-render", plan.CodemodTransforms!);
        Assert.Contains("React-PropTypes-to-prop-types", plan.CodemodTransforms!);
    }

    [Fact]
    public async Task ApplyAsync_UpgradesPackagesAndValidatesBuild_WhenTargetVersionsSupplied()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "react": "^17.0.0" } }""");

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "install ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new ReactStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.React, "Upgrade path", ["upgrade-react-and-dom"], 80,
            ProjectPath: packageJsonPath,
            PackageTargetVersions: new Dictionary<string, string> { ["react"] = "18.2.0" });

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
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "react": "18.2.0" } }""");

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "codemod-a ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new ReactStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.React, "Upgrade path", ["migrate-rendering-api"], 80,
            ProjectPath: packageJsonPath,
            CodemodTransforms: ["react-18-create-root"]);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("npx", runner.Calls[0].FileName);
        Assert.Contains("react-18-create-root", runner.Calls[0].Arguments);
        Assert.Equal(("npm", "run build"), runner.Calls[1]);
        Assert.Equal(80, result.Confidence);
    }

    [Fact]
    public async Task ApplyAsync_DoesNothing_WhenNoPackageVersionsOrTransformsSupplied()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        var runner = new SequencedProcessRunner();
        var adapter = new ReactStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(StackKind.React, "Upgrade path", ["upgrade-react-and-dom"], 80, ProjectPath: packageJsonPath);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Empty(runner.Calls);
        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.ChangedFiles);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsZeroConfidence_WhenProjectPathIsUnresolved()
    {
        var adapter = new ReactStackUpgradeAdapter(new SequencedProcessRunner());
        var plan = new StackUpgradePlan(StackKind.React, "Upgrade path", [], 80, ProjectPath: null);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

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
