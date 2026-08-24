using UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class CentralPackageVersionUpgradeAgentTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-cpm-upgrade-" + Guid.NewGuid());

    private const string DirectoryPackagesPropsTemplate = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="Newtonsoft.Json" Version="12.0.3" />
          </ItemGroup>
        </Project>
        """;

    private const string CsprojTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" />
          </ItemGroup>
        </Project>
        """;

    public CentralPackageVersionUpgradeAgentTests()
    {
        Directory.CreateDirectory(_fixtureDir);
        File.Copy(
            Path.Combine(TestPaths.FindRepositoryRoot(), "NuGet.Config"),
            Path.Combine(_fixtureDir, "NuGet.Config"));
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesCentralPackageVersion_AndRestoreSucceeds_ForRealBump()
    {
        var propsPath = Path.Combine(_fixtureDir, "Directory.Packages.props");
        var csprojPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        await File.WriteAllTextAsync(propsPath, DirectoryPackagesPropsTemplate);
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var agent = new CentralPackageVersionUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new CentralPackageVersionInput(propsPath, csprojPath, new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" }),
            context);

        Assert.True(result.Output.RestoreSucceeded, result.Output.RestoreOutput);
        var update = Assert.Single(result.Output.Updates);
        Assert.Equal("12.0.3", update.OldVersion);
        Assert.Equal("13.0.3", update.NewVersion);

        var savedContent = await File.ReadAllTextAsync(propsPath);
        Assert.Contains("Version=\"13.0.3\"", savedContent);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsPackagesAlreadyAtTargetVersion()
    {
        var propsPath = Path.Combine(_fixtureDir, "Directory2.Packages.props");
        await File.WriteAllTextAsync(propsPath, DirectoryPackagesPropsTemplate);

        var agent = new CentralPackageVersionUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new CentralPackageVersionInput(propsPath, propsPath, new Dictionary<string, string> { ["Newtonsoft.Json"] = "12.0.3" }),
            context);

        Assert.Empty(result.Output.Updates);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenRestoreDidNotSucceed()
    {
        var agent = new CentralPackageVersionUpgradeAgent(new FakeProcessRunner(new ProcessRunResult(1, "", "error")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failedReport = new CentralPackageVersionReport([], false, "error");

        var validation = await agent.ValidateAsync(failedReport, context);

        Assert.False(validation.IsValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDir))
        {
            Directory.Delete(_fixtureDir, recursive: true);
        }
    }
}
