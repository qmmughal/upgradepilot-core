using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.Tests;

public class RepositoryAnalyzerAgentTests
{
    [Fact]
    public async Task ExecuteAsync_FindsProjectsAndSourceFiles_ExcludingBuildOutput()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/Sample.Web/Sample.Web.csproj", "<Project />")
            .AddFile("/repo/Sample.Web/Program.cs", "// entry point")
            .AddFile("/repo/Sample.Web/obj/Sample.Web.AssemblyInfo.cs", "// generated")
            .AddFile("/repo/Sample.Web/bin/Debug/Sample.Web.cs", "// build output");

        var agent = new RepositoryAnalyzerAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/repo", context);

        var project = Assert.Single(result.Output.Projects);
        Assert.Equal("Sample.Web", project.Name);
        Assert.Single(project.SourceFiles);
        Assert.Equal(100, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZeroConfidence_WhenNoProjectsFound()
    {
        var reader = new FakeRepositoryReader();
        var agent = new RepositoryAnalyzerAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync("/empty-repo", context);

        Assert.Empty(result.Output.Projects);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFactInContext()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/A/A.csproj", "<Project />");
        var agent = new RepositoryAnalyzerAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        await agent.ExecuteAsync("/repo", context);

        Assert.NotNull(context.LatestFact("repository-map"));
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenNoProjects()
    {
        var agent = new RepositoryAnalyzerAgent(new FakeRepositoryReader());
        var context = new UpgradeContext(Guid.NewGuid());
        var emptyMap = new RepositoryMap("/empty-repo", []);

        var validation = await agent.ValidateAsync(emptyMap, context);

        Assert.False(validation.IsValid);
    }
}
