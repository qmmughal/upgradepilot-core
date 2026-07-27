using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Agents.Pipeline.Validation.SecurityValidation;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Validation;

public class SecurityValidationAgentTests
{
    private const string HighSeverityJson = """
        {
          "projects": [
            {
              "path": "/repo/Sample.Web/Sample.Web.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    {
                      "id": "Newtonsoft.Json",
                      "resolvedVersion": "9.0.1",
                      "vulnerabilities": [ { "severity": "High", "advisoryurl": "https://example.com/advisory" } ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string LowSeverityJson = """
        {
          "projects": [
            {
              "path": "/repo/Sample.Web/Sample.Web.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    {
                      "id": "SomePackage",
                      "resolvedVersion": "1.0.0",
                      "vulnerabilities": [ { "severity": "Low", "advisoryurl": "https://example.com/advisory" } ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static readonly RepositoryMap SampleMap = new(
        "/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);

    [Fact]
    public async Task ExecuteAsync_BlocksProgression_WhenHighSeverityFindingPresent()
    {
        var agent = new SecurityValidationAgent(new FakeProcessRunner(new ProcessRunResult(0, HighSeverityJson, "")));
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(SampleMap, context);

        Assert.True(result.Output.BlocksProgression);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotBlock_WhenOnlyLowSeverityFindingPresent()
    {
        var agent = new SecurityValidationAgent(new FakeProcessRunner(new ProcessRunResult(0, LowSeverityJson, "")));
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(SampleMap, context);

        Assert.False(result.Output.BlocksProgression);
        Assert.Equal(90, result.Confidence);
        Assert.Single(result.Output.Findings);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenReportBlocksProgression()
    {
        var agent = new SecurityValidationAgent(new FakeProcessRunner(new ProcessRunResult(0, "{}", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var blockingReport = new SecurityReport([new SecurityFinding("X", "Y", "Critical", null)], true);

        var validation = await agent.ValidateAsync(blockingReport, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealDotnetList_NoBlockingFindings_AgainstOwnDomainTestsProject()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "tests", "UpgradePilot.Core.Domain.Tests", "UpgradePilot.Core.Domain.Tests.csproj");
        var map = new RepositoryMap(repoRoot, [new ProjectInfo("UpgradePilot.Core.Domain.Tests", projectPath, [])]);

        var agent = new SecurityValidationAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(map, context);

        Assert.False(result.Output.BlocksProgression);
    }
}
