using UpgradePilot.Core.Agents.Discovery.Ports;
using UpgradePilot.Core.Agents.Pipeline.Knowledge.DocumentationRetrieval;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class DocumentationRetrievalAgentTests
{
    private sealed class FakeReader : IRepositoryReader
    {
        private readonly Dictionary<string, string> _files = new();

        public FakeReader Add(string path, string content)
        {
            _files[path] = content;
            return this;
        }

        public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern) =>
            _files.Keys.Where(k => k.StartsWith(directoryPath, StringComparison.Ordinal));

        public string ReadAllText(string filePath) => _files[filePath];
    }

    [Fact]
    public async Task ExecuteAsync_RanksPassageWithMoreTermMatchesHigher()
    {
        var reader = new FakeReader()
            .Add("/docs/a.md", "Some unrelated content.\n\nSaga saga saga orchestration details here.")
            .Add("/docs/b.md", "Saga mentioned once.\n\nNothing else relevant.");

        var agent = new DocumentationRetrievalAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new DocumentationRetrievalInput("/docs", "saga"), context);

        Assert.Equal(2, result.Output.Passages.Count);
        Assert.Equal("/docs/a.md", result.Output.Passages[0].SourcePath);
        Assert.True(result.Output.Passages[0].Score > result.Output.Passages[1].Score);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZeroConfidence_WhenNoMatches()
    {
        var reader = new FakeReader().Add("/docs/a.md", "Nothing relevant here.");
        var agent = new DocumentationRetrievalAgent(reader);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new DocumentationRetrievalInput("/docs", "nonexistentterm"), context);

        Assert.Empty(result.Output.Passages);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenPassageHasNoSourcePath()
    {
        var agent = new DocumentationRetrievalAgent(new FakeReader());
        var context = new UpgradeContext(Guid.NewGuid());
        var badBundle = new DocumentationBundle([new DocumentPassage("", "text", 5)]);

        var validation = await agent.ValidateAsync(badBundle, context);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RealFilesystem_FindsRealPassageInThisRepositorysArchitectureDocs()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var docsDir = Path.Combine(repoRoot, "docs", "architecture");

        var agent = new DocumentationRetrievalAgent(new LocalFileSystemRepositoryReader());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new DocumentationRetrievalInput(docsDir, "confidence"), context);

        Assert.NotEmpty(result.Output.Passages);
        Assert.Contains(result.Output.Passages, p => p.SourcePath.Contains("agents.md"));
    }
}
