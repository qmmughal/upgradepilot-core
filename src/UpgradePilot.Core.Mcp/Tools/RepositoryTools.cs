using System.ComponentModel;
using ModelContextProtocol.Server;
using UpgradePilot.Core.Agents.Discovery.Ports;

namespace UpgradePilot.Core.Mcp.Tools;

/// <summary>
/// The `repo.*` MCP tools referenced throughout docs/architecture/agents.md as the
/// Discovery-phase tool namespace (§1.4, §4.1-§4.5). This is the real MCP protocol
/// server issue #3 asked for - agent code (Agents.Discovery, Agents.Pipeline) is
/// unchanged; those projects' IRepositoryReader/IProcessRunner ports are exactly what
/// let this server exist without touching a single agent implementation.
/// </summary>
[McpServerToolType]
public static class RepositoryTools
{
    private static readonly IRepositoryReader Reader = new LocalFileSystemRepositoryReader();

    [McpServerTool(Name = "repo_readFile"), Description("Read the full text content of a file at the given absolute path.")]
    public static string ReadFile([Description("Absolute path to the file")] string path) =>
        Reader.ReadAllText(path);

    [McpServerTool(Name = "repo_stat"), Description("List files under a directory (recursively) matching a search pattern, e.g. '*.csproj'.")]
    public static IReadOnlyList<string> Stat(
        [Description("Directory to search")] string directoryPath,
        [Description("Search pattern, e.g. *.cs or *.csproj")] string searchPattern) =>
        Reader.EnumerateFiles(directoryPath, searchPattern).ToList();

    [McpServerTool(Name = "repo_grep"), Description("Search for a literal substring across files under a directory matching a search pattern, returning matching file paths and line numbers.")]
    public static IReadOnlyList<string> Grep(
        [Description("Directory to search")] string directoryPath,
        [Description("Search pattern for files to scan, e.g. *.cs")] string searchPattern,
        [Description("Literal substring to search for")] string query)
    {
        var matches = new List<string>();

        foreach (var file in Reader.EnumerateFiles(directoryPath, searchPattern))
        {
            var lines = Reader.ReadAllText(file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.Ordinal))
                {
                    matches.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return matches;
    }
}
