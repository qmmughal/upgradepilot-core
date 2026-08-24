using ModelContextProtocol.Client;

namespace UpgradePilot.Core.Mcp.Tests;

/// <summary>
/// Spawns the real compiled MCP server as a child process over stdio and talks to it
/// with the official MCP client SDK - genuine protocol-level test, not a mock.
/// </summary>
public class McpServerTests : IAsyncLifetime
{
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "upgradepilot-mcp-test",
            Command = "dotnet",
            Arguments = [TestPaths.FindMcpServerDll()],
        });

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public async Task ListTools_ExposesRepoAndBuildTools()
    {
        var tools = await _client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("repo_readFile", names);
        Assert.Contains("repo_stat", names);
        Assert.Contains("repo_grep", names);
        Assert.Contains("build_compile", names);
        Assert.Contains("test_run", names);
        Assert.Contains("build_compileFrontend", names);
        Assert.Contains("test_runFrontend", names);
        Assert.Contains("build_npmInstall", names);
    }

    [Fact]
    public async Task CallTool_RepoStat_FindsCsprojFilesInThisRepository()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();

        var result = await _client.CallToolAsync(
            "repo_stat",
            new Dictionary<string, object?>
            {
                ["directoryPath"] = repoRoot,
                ["searchPattern"] = "UpgradePilot.Core.Domain.csproj",
            });

        Assert.NotEqual(true, result.IsError);
        var text = string.Join('\n', result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        Assert.Contains("UpgradePilot.Core.Domain.csproj", text);
    }

    [Fact]
    public async Task CallTool_RepoReadFile_ReturnsRealFileContent()
    {
        var repoRoot = TestPaths.FindRepositoryRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");

        var result = await _client.CallToolAsync(
            "repo_readFile",
            new Dictionary<string, object?> { ["path"] = readmePath });

        Assert.NotEqual(true, result.IsError);
        var text = string.Join('\n', result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        Assert.Contains("UpgradePilot", text);
    }
}
