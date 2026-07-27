using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Discovery.Tests.Fakes;
using UpgradePilot.Core.Agents.Discovery.VersionDetector;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.Tests;

public class VersionDetectorAgentTests
{
    private const string CsprojWithAbpLegacy = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Abp.AspNetCore" Version="9.2.0" />
          </ItemGroup>
        </Project>
        """;

    private const string CsprojWithAbpVNext = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net9.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Volo.Abp.AspNetCore.Mvc" Version="8.3.0" />
          </ItemGroup>
        </Project>
        """;

    private const string PlainCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public async Task ExecuteAsync_DetectsTargetFrameworkAndAbpLegacyPackage()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/Sample.Web/Sample.Web.csproj", CsprojWithAbpLegacy);
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);

        var agent = new VersionDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(map, context);

        Assert.Contains(result.Output.Signals, s => s.Value == "net8.0");
        Assert.Contains(result.Output.Signals, s => s.Source == "Abp.AspNetCore" && s.Value == "9.2.0");
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsAbpVNextPackage()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/Sample.Web/Sample.Web.csproj", CsprojWithAbpVNext);
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);

        var agent = new VersionDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(map, context);

        Assert.Contains(result.Output.Signals, s => s.Source == "Volo.Abp.AspNetCore.Mvc" && s.Value == "8.3.0");
    }

    [Fact]
    public async Task ExecuteAsync_LowConfidence_WhenNoAbpSignalsFound()
    {
        var reader = new FakeRepositoryReader()
            .AddFile("/repo/Sample.Web/Sample.Web.csproj", PlainCsproj);
        var map = new RepositoryMap("/repo", [new ProjectInfo("Sample.Web", "/repo/Sample.Web/Sample.Web.csproj", [])]);

        var agent = new VersionDetectorAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(map, context);

        Assert.Single(result.Output.Signals);
        Assert.Equal(90, result.Confidence); // target framework alone still counts as a resolved signal
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenNoSignalsResolved()
    {
        var agent = new VersionDetectorAgent(new FakeRepositoryReader());
        var context = new UpgradeContext(Guid.NewGuid());

        var validation = await agent.ValidateAsync(new VersionManifest([]), context);

        Assert.False(validation.IsValid);
    }
}
