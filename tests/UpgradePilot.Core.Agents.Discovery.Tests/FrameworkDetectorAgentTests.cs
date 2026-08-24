using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.Tests.Fakes;
using UpgradePilot.Core.Agents.Discovery.VersionDetector;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.Tests;

public class FrameworkDetectorAgentTests
{
    [Fact]
    public async Task ExecuteAsync_ClassifiesAsAbpLegacy_WhenLegacyAbpSignalPresent()
    {
        var reader = new FakeRepositoryReader();
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);
        var versions = new VersionManifest([new FrameworkVersionSignal("Abp.AspNetCore", "9.2.0", 100)]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        var classification = Assert.Single(result.Output.Classifications);
        Assert.Equal(DetectedFramework.AbpFrameworkLegacy, classification.Framework);
    }

    [Fact]
    public async Task ExecuteAsync_ClassifiesAsAbpVNext_WhenVoloAbpSignalPresent()
    {
        var reader = new FakeRepositoryReader();
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);
        var versions = new VersionManifest([new FrameworkVersionSignal("Volo.Abp.AspNetCore.Mvc", "8.3.0", 100)]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        var classification = Assert.Single(result.Output.Classifications);
        Assert.Equal(DetectedFramework.AbpFrameworkVNext, classification.Framework);
    }

    [Fact]
    public async Task ExecuteAsync_ClassifiesAsUnknown_WhenNoAbpSignalsPresent()
    {
        var reader = new FakeRepositoryReader();
        var map = new RepositoryMap("/repo", [new ProjectInfo("Plain.Web", "/repo/Plain.Web/Plain.Web.csproj", [])]);
        var versions = new VersionManifest([]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        var classification = Assert.Single(result.Output.Classifications);
        Assert.Equal(DetectedFramework.Unknown, classification.Framework);
        Assert.Equal(50, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsAngularFrontEnd_WhenAngularJsonPresent()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/angular/angular.json", "{}");
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web.Host", "/repo/Sample.Web.Host/Sample.Web.Host.csproj", [])]);
        var versions = new VersionManifest([new FrameworkVersionSignal("Abp.AspNetCore", "9.2.0", 100)]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.True(result.Output.HasAngularFrontEnd);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsDotNetStack_WhenRepoContainsNetProjects()
    {
        var reader = new FakeRepositoryReader();
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);
        var versions = new VersionManifest([new FrameworkVersionSignal("Volo.Abp.AspNetCore.Mvc", "8.3.0", 100)]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(StackKind.DotNet, result.Output.StackKind);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsReactStack_WhenPackageJsonHasReactDependency()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/package.json", "{\"dependencies\":{\"react\":\"^18.0.0\"}} ");
        var map = new RepositoryMap("/repo", [new ProjectInfo("frontend", "/repo/frontend", [])]);
        var versions = new VersionManifest([]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(StackKind.React, result.Output.StackKind);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsNextJsStack_WhenPackageJsonHasNextDependency()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/package.json", "{\"dependencies\":{\"next\":\"14.2.0\"}} ");
        var map = new RepositoryMap("/repo", [new ProjectInfo("webapp", "/repo/webapp", [])]);
        var versions = new VersionManifest([]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(StackKind.NextJs, result.Output.StackKind);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsAppRouter_WhenLayoutFilePresent()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/package.json", "{\"dependencies\":{\"next\":\"14.2.0\"}} ")
            .AddFile("/repo/app/layout.tsx", "export default function Layout() {}");
        var map = new RepositoryMap("/repo", [new ProjectInfo("webapp", "/repo/webapp", [])]);
        var versions = new VersionManifest([]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(NextJsRoutingMode.App, result.Output.NextJsRoutingMode);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsPagesRouter_WhenAppFilePresent()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/package.json", "{\"dependencies\":{\"next\":\"14.2.0\"}} ")
            .AddFile("/repo/pages/_app.tsx", "export default function App() {}");
        var map = new RepositoryMap("/repo", [new ProjectInfo("webapp", "/repo/webapp", [])]);
        var versions = new VersionManifest([]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(NextJsRoutingMode.Pages, result.Output.NextJsRoutingMode);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesRoutingModeUnknown_ForNonNextJsStacks()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/package.json", "{\"dependencies\":{\"react\":\"^18.0.0\"}} ");
        var map = new RepositoryMap("/repo", [new ProjectInfo("frontend", "/repo/frontend", [])]);
        var versions = new VersionManifest([]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(NextJsRoutingMode.Unknown, result.Output.NextJsRoutingMode);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsMixedStack_WhenRepoContainsDotNetAndReact()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/package.json", "{\"dependencies\":{\"react\":\"^18.0.0\"}}")
            .AddFile("/repo/src/App.Api/App.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var map = new RepositoryMap("/repo", [
            new ProjectInfo("App.Api", "/repo/src/App.Api/App.Api.csproj", []),
            new ProjectInfo("frontend", "/repo/package.json", [])
        ]);
        var versions = new VersionManifest([new FrameworkVersionSignal("Volo.Abp.AspNetCore.Mvc", "8.3.0", 100)]);

        var agent = new FrameworkDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new FrameworkDetectorInput(map, versions), context);

        Assert.Equal(StackKind.Mixed, result.Output.StackKind);
    }
}
