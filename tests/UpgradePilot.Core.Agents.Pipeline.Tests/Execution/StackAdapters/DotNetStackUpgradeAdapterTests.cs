using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;
using UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution.StackAdapters;

public class DotNetStackUpgradeAdapterTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-dotnet-adapter-" + Guid.NewGuid());

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

    public DotNetStackUpgradeAdapterTests()
    {
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task ApplyAsync_UpgradesPackagesAndValidatesBuild_WhenTargetVersionsSupplied()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.DotNet, "Upgrade path", ["upgrade-nuget-packages"], 80,
            ProjectPath: csprojPath,
            PackageTargetVersions: new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" });

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("dotnet", runner.Calls[0].FileName);
        Assert.Contains("restore", runner.Calls[0].Arguments);
        Assert.Equal("dotnet", runner.Calls[1].FileName);
        Assert.Contains("build", runner.Calls[1].Arguments);
        Assert.Contains(csprojPath, result.Output.ChangedFiles);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ApplyAsync_UpgradesTargetFramework_WhenDotNetTargetFrameworkSupplied()
    {
        var csprojPath = Path.Combine(_fixtureDir, "FixtureTfm.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.DotNet, "Upgrade path", ["analyze-dotnet-version-matrix"], 80,
            ProjectPath: csprojPath,
            DotNetTargetFramework: "net10.0");

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Contains(csprojPath, result.Output.ChangedFiles);
        Assert.Equal(90, result.Confidence);

        var savedContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", savedContent);
    }

    [Fact]
    public async Task ApplyAsync_IteratesAllProjects_WhenDotNetProjectFilePathsSupplied()
    {
        var csprojPathA = Path.Combine(_fixtureDir, "ProjectA.csproj");
        var csprojPathB = Path.Combine(_fixtureDir, "ProjectB.csproj");
        await File.WriteAllTextAsync(csprojPathA, CsprojTemplate);
        await File.WriteAllTextAsync(csprojPathB, CsprojTemplate);

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "build ok", ""),
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.DotNet, "Upgrade path", ["upgrade-nuget-packages"], 80,
            ProjectPath: csprojPathA,
            PackageTargetVersions: new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" },
            DotNetProjectFilePaths: [csprojPathA, csprojPathB]);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(4, runner.Calls.Count);
        Assert.Contains(csprojPathA, result.Output.ChangedFiles);
        Assert.Contains(csprojPathB, result.Output.ChangedFiles);
    }

    [Fact]
    public async Task BuildUpgradePlanAsync_DerivesPackageTargetVersions_FromRealDependencyAnalysis()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        const string listOutput = """
            { "version": 1, "projects": [ { "path": "Fixture.csproj", "frameworks": [ { "framework": "net8.0",
              "topLevelPackages": [ { "id": "Newtonsoft.Json", "requestedVersion": "12.0.3", "resolvedVersion": "12.0.3" } ] } ] } ] }
            """;
        const string outdatedOutput = """
            { "version": 1, "projects": [ { "path": "Fixture.csproj", "frameworks": [ { "framework": "net8.0",
              "topLevelPackages": [ { "id": "Newtonsoft.Json", "resolvedVersion": "12.0.3", "latestVersion": "13.0.3" } ] } ] } ] }
            """;
        const string noVulnerabilitiesOutput = """
            { "version": 1, "projects": [ { "path": "Fixture.csproj" } ] }
            """;

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, listOutput, ""),
            new ProcessRunResult(0, outdatedOutput, ""),
            new ProcessRunResult(0, noVulnerabilitiesOutput, ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var repo = new RepositoryMap(_fixtureDir, [new ProjectInfo("Fixture", csprojPath, [])]);
        var profile = new FrameworkProfile([], false, StackKind.DotNet);

        var plan = await adapter.BuildUpgradePlanAsync(repo, profile, new UpgradeContext(Guid.NewGuid()));

        Assert.NotNull(plan.PackageTargetVersions);
        Assert.Equal("13.0.3", plan.PackageTargetVersions!["Newtonsoft.Json"]);
    }

    [Fact]
    public async Task ApplyAsync_UsesCentralPackageVersionAgent_WhenDirectoryPackagesPropsPresent()
    {
        var propsPath = Path.Combine(_fixtureDir, "Directory.Packages.props");
        var csprojPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        await File.WriteAllTextAsync(propsPath, """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="12.0.3" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """);

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.DotNet, "Upgrade path", ["upgrade-nuget-packages"], 80,
            ProjectPath: csprojPath,
            PackageTargetVersions: new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" });

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains(propsPath, result.Output.ChangedFiles);
        Assert.DoesNotContain(csprojPath, result.Output.ChangedFiles);

        var savedProps = await File.ReadAllTextAsync(propsPath);
        Assert.Contains("Version=\"13.0.3\"", savedProps);
    }

    [Fact]
    public async Task ApplyAsync_AppliesRenames_ThenValidatesBuild_WhenDotNetRenameTargetsSupplied()
    {
        var csprojPath = Path.Combine(_fixtureDir, "FixtureRename.csproj");
        var sourcePath = Path.Combine(_fixtureDir, "SampleService.cs");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);
        await File.WriteAllTextAsync(sourcePath, "public class SampleService { public string OldMethodName() => \"v\"; }");

        var runner = new SequencedProcessRunner(new ProcessRunResult(0, "build ok", ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.DotNet, "Upgrade path", ["refactor-breaking-aspnet-core-apis"], 80,
            ProjectPath: csprojPath,
            DotNetRenameTargets: [new DotNetRenameTarget(sourcePath, [new RenameRule("OldMethodName", "NewMethodName")])]);

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Single(runner.Calls);
        Assert.Contains(sourcePath, result.Output.ChangedFiles);

        var savedSource = await File.ReadAllTextAsync(sourcePath);
        Assert.Contains("NewMethodName", savedSource);
    }

    [Fact]
    public async Task ApplyAsync_RunsMigration_ThenValidatesBuild_WhenDotNetMigrationNameSupplied()
    {
        var csprojPath = Path.Combine(_fixtureDir, "FixtureMigration.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var runner = new SequencedProcessRunner(
            new ProcessRunResult(0, "restore ok", ""),
            new ProcessRunResult(0, "ef ok", ""),
            new ProcessRunResult(0, "build ok", ""));
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(
            StackKind.DotNet, "Upgrade path", ["apply-ef-core-migration-planning"], 80,
            ProjectPath: csprojPath,
            DotNetMigrationName: "AddUpgradeColumn");

        var result = await adapter.ApplyAsync(plan, new UpgradeContext(Guid.NewGuid()));

        Assert.Equal(3, runner.Calls.Count);
        Assert.Contains("ef migrations add AddUpgradeColumn", runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task ApplyAsync_DoesNothing_WhenNoPackageVersionsSupplied()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture2.csproj");
        var runner = new SequencedProcessRunner();
        var adapter = new DotNetStackUpgradeAdapter(runner);
        var plan = new StackUpgradePlan(StackKind.DotNet, "Upgrade path", ["upgrade-nuget-packages"], 80, ProjectPath: csprojPath);

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
