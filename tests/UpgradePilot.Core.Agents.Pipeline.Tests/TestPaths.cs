namespace UpgradePilot.Core.Agents.Pipeline.Tests;

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
}
