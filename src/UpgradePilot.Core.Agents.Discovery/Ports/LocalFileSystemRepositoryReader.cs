namespace UpgradePilot.Core.Agents.Discovery.Ports;

/// <summary>
/// Default adapter for <see cref="IRepositoryReader"/>: reads directly off the local
/// filesystem. This is what upgradepilot-cli uses for local, single-user runs (see
/// docs/architecture/open-core-boundary.md §1) — no sandboxing beyond what the OS
/// process already has.
/// </summary>
public sealed class LocalFileSystemRepositoryReader : IRepositoryReader
{
    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern) =>
        Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories);

    public string ReadAllText(string filePath) => File.ReadAllText(filePath);
}
