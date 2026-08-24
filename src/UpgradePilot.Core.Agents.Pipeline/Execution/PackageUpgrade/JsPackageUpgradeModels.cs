namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

public sealed record JsPackageUpgradeInput(string PackageJsonPath, IReadOnlyDictionary<string, string> TargetVersions);

public sealed record JsPackageUpdateEntry(string PackageId, string OldVersion, string NewVersion, bool WasDevDependency);

public sealed record JsPackageUpgradeReport(
    IReadOnlyList<JsPackageUpdateEntry> Updates,
    bool InstallSucceeded,
    string InstallOutput);
