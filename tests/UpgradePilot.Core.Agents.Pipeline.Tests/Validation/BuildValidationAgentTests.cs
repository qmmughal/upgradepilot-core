using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Validation;

public class BuildValidationAgentTests
{
    private const string FailingBuildOutput = """
        Restore complete.
        D:\repo\Sample.Web\Program.cs(10,5): error CS1002: ; expected [D:\repo\Sample.Web\Sample.Web.csproj]
        Build FAILED.
        """;

    [Fact]
    public async Task ExecuteAsync_ParsesCompilerErrors_FromFakeOutput()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, FailingBuildOutput, ""));
        var agent = new BuildValidationAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/Sample.Web/Sample.Web.csproj", context);

        Assert.False(result.Output.Succeeded);
        var error = Assert.Single(result.Output.Errors);
        Assert.Equal("CS1002", error.Code);
        Assert.Equal(10, error.Line);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_Succeeds_WhenExitCodeZeroAndNoErrors()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "Build succeeded.", ""));
        var agent = new BuildValidationAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo/Sample.Web/Sample.Web.csproj", context);

        Assert.True(result.Output.Succeeded);
        Assert.Equal(100, result.Confidence);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenBuildDidNotSucceed()
    {
        var agent = new BuildValidationAgent(new FakeProcessRunner(new ProcessRunResult(1, "", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failedResult = new BuildResult(false, [new BuildDiagnostic("CS1002", "; expected", "Program.cs", 10)], "");

        var validation = await agent.ValidateAsync(failedResult, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealDotnetBuild_SucceedsAgainstOwnDomainProject()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "src", "UpgradePilot.Core.Domain", "UpgradePilot.Core.Domain.csproj");

        var agent = new BuildValidationAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(projectPath, context);

        Assert.True(result.Output.Succeeded, result.Output.RawOutput);
    }
}
