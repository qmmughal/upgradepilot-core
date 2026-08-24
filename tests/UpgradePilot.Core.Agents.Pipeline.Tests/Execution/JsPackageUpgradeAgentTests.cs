using UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class JsPackageUpgradeAgentTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-js-pkg-upgrade-" + Guid.NewGuid());

    private const string PackageJsonTemplate = """
        {
          "name": "fixture",
          "version": "1.0.0",
          "dependencies": {
            "is-number": "^6.0.0"
          }
        }
        """;

    public JsPackageUpgradeAgentTests()
    {
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesDependencyVersion_AndInstallSucceeds_ForRealPackageBump()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package.json");
        await File.WriteAllTextAsync(packageJsonPath, PackageJsonTemplate);

        var agent = new JsPackageUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new JsPackageUpgradeInput(packageJsonPath, new Dictionary<string, string> { ["is-number"] = "7.0.0" }),
            context);

        Assert.True(result.Output.InstallSucceeded, result.Output.InstallOutput);
        var update = Assert.Single(result.Output.Updates);
        Assert.Equal("^6.0.0", update.OldVersion);
        Assert.Equal("^7.0.0", update.NewVersion, ignoreCase: false);
        Assert.False(update.WasDevDependency);

        var savedContent = await File.ReadAllTextAsync(packageJsonPath);
        Assert.Contains("\"is-number\": \"^7.0.0\"", savedContent);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsPackagesAlreadyAtTargetVersion()
    {
        var packageJsonPath = Path.Combine(_fixtureDir, "package2.json");
        await File.WriteAllTextAsync(packageJsonPath, PackageJsonTemplate);

        var agent = new JsPackageUpgradeAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new JsPackageUpgradeInput(packageJsonPath, new Dictionary<string, string> { ["is-number"] = "^6.0.0" }),
            context);

        Assert.Empty(result.Output.Updates);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenInstallDidNotSucceed()
    {
        var agent = new JsPackageUpgradeAgent(new FakeProcessRunner(new ProcessRunResult(1, "", "error")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failedReport = new JsPackageUpgradeReport([], false, "error");

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
