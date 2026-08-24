using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution.StackAdapters;

public class MixedStackUpgradeAdapterTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-mixed-adapter-" + Guid.NewGuid());

    private const string CsprojTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />
          </ItemGroup>
        </Project>
        """;

    public MixedStackUpgradeAdapterTests()
    {
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task BuildUpgradePlanAsync_ResolvesBothBackendAndFrontendProjectPaths()
    {
        var backendDir = Path.Combine(_fixtureDir, "backend");
        var frontendDir = Path.Combine(_fixtureDir, "frontend");
        Directory.CreateDirectory(backendDir);
        Directory.CreateDirectory(frontendDir);

        var csprojPath = Path.Combine(backendDir, "Backend.csproj");
        var packageJsonPath = Path.Combine(frontendDir, "package.json");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "react": "^18.0.0" } }""");

        var adapter = new MixedStackUpgradeAdapter(new FakeProcessRunner(new ProcessRunResult(0, "", "")));
        var profile = new FrameworkProfile([], false, StackKind.Mixed);
        var repo = new RepositoryMap(_fixtureDir, [
            new ProjectInfo("backend", csprojPath, []),
            new ProjectInfo("frontend", packageJsonPath, [])
        ]);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(csprojPath, plan.ProjectPath);
        Assert.Equal(packageJsonPath, plan.MixedFrontendProjectPath);
        Assert.Equal(StackKind.React, plan.MixedFrontendKind);
    }

    [Fact]
    public async Task ApplyAsync_RunsBackendThenFrontend_AggregatingChangedFilesAndMinConfidence()
    {
        var backendDir = Path.Combine(_fixtureDir, "backend");
        var frontendDir = Path.Combine(_fixtureDir, "frontend");
        Directory.CreateDirectory(backendDir);
        Directory.CreateDirectory(frontendDir);

        var csprojPath = Path.Combine(backendDir, "Backend.csproj");
        var packageJsonPath = Path.Combine(frontendDir, "package.json");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);
        await File.WriteAllTextAsync(packageJsonPath, """{ "dependencies": { "react": "^17.0.0" } }""");

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "build ok", ""),
            new ProcessRunResult(0, "install ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new MixedStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.Mixed, "Upgrade path", ["coordinate-migration-order"], 80,
            ProjectPath: csprojPath,
            PackageTargetVersions: new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" },
            MixedFrontendKind: StackKind.React,
            MixedFrontendProjectPath: packageJsonPath,
            FrontendPackageTargetVersions: new Dictionary<string, string> { ["react"] = "18.2.0" });

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(4, runner.Calls.Count);
        Assert.Contains(csprojPath, result.Output.ChangedFiles);
        Assert.Contains(packageJsonPath, result.Output.ChangedFiles);
        Assert.Contains("[backend]", result.Output.Summary);
        Assert.Contains("[frontend/React]", result.Output.Summary);
    }

    [Fact]
    public async Task ApplyAsync_SkipsFrontend_WhenFrontendKindCouldNotBeResolved()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Backend.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var runner = new SequencedProcessRunner();
        var adapter = new MixedStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.Mixed, "Upgrade path", ["coordinate-migration-order"], 80,
            ProjectPath: csprojPath);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Contains("frontend half skipped", result.Output.Summary);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDir))
        {
            Directory.Delete(_fixtureDir, recursive: true);
        }
    }
}
