using UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class PackageUpgradeAgentTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-pkg-upgrade-" + Guid.NewGuid());

    private const string CsprojTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />
          </ItemGroup>
        </Project>
        """;

    public PackageUpgradeAgentTests()
    {
        Directory.CreateDirectory(_fixtureDir);
        File.Copy(
            Path.Combine(TestPaths.FindRepositoryRoot(), "NuGet.Config"),
            Path.Combine(_fixtureDir, "NuGet.Config"));
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesVersionAttribute_AndRestoreSucceeds_ForRealPackageBump()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var agent = new PackageUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new PackageUpgradeInput(csprojPath, new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" }),
            context);

        Assert.True(result.Output.RestoreSucceeded, result.Output.RestoreOutput);
        var update = Assert.Single(result.Output.Updates);
        Assert.Equal("12.0.3", update.OldVersion);
        Assert.Equal("13.0.3", update.NewVersion);

        var savedContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("Version=\"13.0.3\"", savedContent);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsPackagesAlreadyAtTargetVersion()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture2.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var agent = new PackageUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new PackageUpgradeInput(csprojPath, new Dictionary<string, string> { ["Newtonsoft.Json"] = "12.0.3" }),
            context);

        Assert.Empty(result.Output.Updates);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenRestoreDidNotSucceed()
    {
        var agent = new PackageUpgradeAgent(new FakeProcessRunner(new ProcessRunResult(1, "", "error")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failedReport = new PackageUpgradeReport([], false, "error");

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
