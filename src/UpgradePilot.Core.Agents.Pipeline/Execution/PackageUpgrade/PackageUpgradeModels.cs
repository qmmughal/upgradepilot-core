namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

public sealed record PackageUpgradeInput(string ProjectFilePath, IReadOnlyDictionary<string, string> TargetVersions);

public sealed record PackageUpdateEntry(string PackageId, string OldVersion, string NewVersion);

public sealed record PackageUpgradeReport(
    IReadOnlyList<PackageUpdateEntry> Updates,
    bool RestoreSucceeded,
    string RestoreOutput);
