using UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class DotNetTargetFrameworkUpgradeAgentTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-tfm-upgrade-" + Guid.NewGuid());

    private const string CsprojTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    public DotNetTargetFrameworkUpgradeAgentTests()
    {
        Directory.CreateDirectory(_fixtureDir);
        File.Copy(
            Path.Combine(TestPaths.FindRepositoryRoot(), "NuGet.Config"),
            Path.Combine(_fixtureDir, "NuGet.Config"));
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesTargetFrameworkElement_AndRestoreSucceeds_ForRealBump()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var agent = new DotNetTargetFrameworkUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new DotNetTargetFrameworkInput(csprojPath, "net10.0"), context);

        Assert.True(result.Output.RestoreSucceeded, result.Output.RestoreOutput);
        Assert.True(result.Output.Changed);
        Assert.Equal("net8.0", result.Output.OldTargetFramework);
        Assert.Equal("net10.0", result.Output.NewTargetFramework);

        var savedContent = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", savedContent);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsNoChange_WhenAlreadyAtTargetFramework()
    {
        var csprojPath = Path.Combine(_fixtureDir, "Fixture2.csproj");
        await File.WriteAllTextAsync(csprojPath, CsprojTemplate);

        var agent = new DotNetTargetFrameworkUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new DotNetTargetFrameworkInput(csprojPath, "net8.0"), context);

        Assert.False(result.Output.Changed);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsZeroConfidence_ForMultiTargetProjects()
    {
        var csprojPath = Path.Combine(_fixtureDir, "MultiTarget.csproj");
        await File.WriteAllTextAsync(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);

        var agent = new DotNetTargetFrameworkUpgradeAgent(new FakeProcessRunner(new ProcessRunResult(0, "", "")));
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new DotNetTargetFrameworkInput(csprojPath, "net10.0"), context);

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
