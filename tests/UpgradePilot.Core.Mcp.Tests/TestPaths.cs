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

        // Match whatever configuration this test assembly itself was built as (Debug
        // locally, Release in CI - see ci.yml) rather than hardcoding one, since the Mcp
        // project is always built alongside the tests with the same configuration.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var dllPath = Path.Combine(repoRoot, "src", "UpgradePilot.Core.Mcp", "bin", configuration, "net10.0", "UpgradePilot.Core.Mcp.dll");

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(
                $"MCP server DLL not found at '{dllPath}'. Build UpgradePilot.Core.Mcp before running this test.", dllPath);
        }

        return dllPath;
    }
}
