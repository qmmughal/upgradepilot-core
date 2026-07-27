using UpgradePilot.Core.Agents.Discovery.Ports;

namespace UpgradePilot.Core.Agents.Discovery.Tests.Fakes;

/// <summary>In-memory <see cref="IRepositoryReader"/> so agent tests don't touch real disk.</summary>
public sealed class FakeRepositoryReader : IRepositoryReader
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public FakeRepositoryReader AddFile(string path, string content)
    {
        _files[Normalize(path)] = content;
        return this;
    }

    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern)
    {
        var normalizedDir = Normalize(directoryPath);
        var extension = searchPattern.TrimStart('*');

        return _files.Keys
            .Where(path => path.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public string ReadAllText(string filePath) => _files[Normalize(filePath)];

    private static string Normalize(string path) => path.Replace('\\', '/');
}
