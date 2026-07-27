namespace UpgradePilot.Core.Agents.Discovery.Ports;

/// <summary>
/// How Discovery-phase agents read repository content. This is the in-process stand-in
/// for the `repo.*` MCP tools (repo.readFile, repo.grep, repo.stat) described in
/// docs/architecture/agents.md — once upgradepilot-mcp exists, an adapter that calls those
/// tools over MCP replaces <see cref="LocalFileSystemRepositoryReader"/> without any
/// agent code changing.
/// </summary>
public interface IRepositoryReader
{
    IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern);

    string ReadAllText(string filePath);
}
