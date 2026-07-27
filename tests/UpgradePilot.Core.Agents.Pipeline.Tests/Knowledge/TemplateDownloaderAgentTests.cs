using UpgradePilot.Core.Agents.Pipeline.Knowledge.TemplateDownloader;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class TemplateDownloaderAgentTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "upgradepilot-template-cache-" + Guid.NewGuid());

    [Fact]
    public async Task ExecuteAsync_ReturnsZeroConfidence_WhenCloneFails()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(128, "", "fatal: repository not found"));
        var agent = new TemplateDownloaderAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new TemplateFetchInput("https://example.invalid/no-such-repo.git", "v1.0", _cacheDir), context);

        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Output.ContentHash);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenContentHashMissing()
    {
        var agent = new TemplateDownloaderAgent(new FakeProcessRunner(new ProcessRunResult(0, "", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var badBaseline = new TemplateBaseline(_cacheDir, "v1.0", "");

        var validation = await agent.ValidateAsync(badBaseline, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealClone_FetchesAndHashVerifiesAPublicRepo()
    {
        var agent = new TemplateDownloaderAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new TemplateFetchInput("https://github.com/octocat/Hello-World.git", "master", _cacheDir), context);

        Assert.Equal(100, result.Confidence);
        Assert.NotEmpty(result.Output.ContentHash);
        Assert.True(Directory.Exists(result.Output.LocalPath));
        Assert.True(Directory.EnumerateFileSystemEntries(result.Output.LocalPath).Any());

        var validation = await agent.ValidateAsync(result.Output, context);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_SecondCall_ReusesCacheWithMatchingHash()
    {
        var agent = new TemplateDownloaderAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());
        var input = new TemplateFetchInput("https://github.com/octocat/Hello-World.git", "master", _cacheDir);

        var first = await agent.ExecuteAsync(input, context);
        var second = await agent.ExecuteAsync(input, context);

        Assert.Equal(first.Output.ContentHash, second.Output.ContentHash);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.EnumerateFiles(_cacheDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_cacheDir, recursive: true);
        }
    }
}
