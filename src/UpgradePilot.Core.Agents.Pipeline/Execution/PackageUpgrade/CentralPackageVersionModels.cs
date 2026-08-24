namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

public sealed record CentralPackageVersionInput(
    string DirectoryPackagesPropsPath,
    string RestoreTargetPath,
    IReadOnlyDictionary<string, string> TargetVersions);

public sealed record CentralPackageVersionReport(
    IReadOnlyList<PackageUpdateEntry> Updates,
    bool RestoreSucceeded,
    string RestoreOutput);
