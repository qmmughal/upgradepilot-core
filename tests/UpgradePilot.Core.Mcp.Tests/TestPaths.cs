namespace UpgradePilot.Core.Mcp.Tests;

internal static class TestPaths
{
    public static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !dir.GetFiles("UpgradePilot.slnx").Any())
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root (UpgradePilot.slnx) from " + AppContext.BaseDirectory);
    }

    public static string FindMcpServerDll()
    {
        var repoRoot = FindRepositoryRoot();
        var dllPath = Path.Combine(repoRoot, "src", "UpgradePilot.Core.Mcp", "bin", "Debug", "net10.0", "UpgradePilot.Core.Mcp.dll");

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(
                $"MCP server DLL not found at '{dllPath}'. Build UpgradePilot.Core.Mcp before running this test.", dllPath);
        }

        return dllPath;
    }
}
