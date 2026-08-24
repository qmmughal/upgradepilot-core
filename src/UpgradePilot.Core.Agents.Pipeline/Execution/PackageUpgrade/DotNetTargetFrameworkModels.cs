namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

public sealed record DotNetTargetFrameworkInput(string ProjectFilePath, string TargetFramework);

public sealed record DotNetTargetFrameworkReport(
    string ProjectFilePath,
    string? OldTargetFramework,
    string NewTargetFramework,
    bool Changed,
    bool RestoreSucceeded,
    string RestoreOutput);
