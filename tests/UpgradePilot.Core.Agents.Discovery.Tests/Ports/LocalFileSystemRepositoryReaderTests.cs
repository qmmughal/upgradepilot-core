using UpgradePilot.Core.Agents.Discovery.Ports;

namespace UpgradePilot.Core.Agents.Discovery.Tests.Ports;

public class LocalFileSystemRepositoryReaderTests : IDisposable
{
    private readonly string _tempDir;

    public LocalFileSystemRepositoryReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "upgradepilot-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_tempDir, "Sample.Web"));
        File.WriteAllText(Path.Combine(_tempDir, "Sample.Web", "Sample.Web.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_tempDir, "Sample.Web", "Program.cs"), "// entry point");
    }

    [Fact]
    public void EnumerateFiles_FindsFilesRecursively()
    {
        var reader = new LocalFileSystemRepositoryReader();

        var csprojFiles = reader.EnumerateFiles(_tempDir, "*.csproj").ToList();

        Assert.Single(csprojFiles);
    }

    [Fact]
    public void ReadAllText_ReturnsFileContent()
    {
        var reader = new LocalFileSystemRepositoryReader();
        var filePath = Path.Combine(_tempDir, "Sample.Web", "Program.cs");

        Assert.Equal("// entry point", reader.ReadAllText(filePath));
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }
}
