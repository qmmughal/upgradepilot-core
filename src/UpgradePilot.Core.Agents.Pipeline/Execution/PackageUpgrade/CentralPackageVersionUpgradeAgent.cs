using System.Xml.Linq;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

/// <summary>
/// The Central Package Management (CPM) counterpart to <see cref="PackageUpgradeAgent"/>.
/// Under CPM (`Directory.Packages.props` with `ManagePackageVersionsCentrally=true`),
/// per-project `.csproj` PackageReference elements normally carry no Version attribute
/// at all - the version lives in one shared `&lt;PackageVersion Include="X" Version="Y" /&gt;`
/// element instead. Editing the .csproj in that setup would be a no-op (or wrong); this
/// agent edits the shared props file once instead, then verifies with a real
/// `dotnet restore` against a representative project, same discipline as every other
/// package-upgrade agent in this codebase.
/// </summary>
public sealed class CentralPackageVersionUpgradeAgent : IUpgradePilotAgent<CentralPackageVersionInput, CentralPackageVersionReport>
{
    private readonly IProcessRunner _processRunner;

    public CentralPackageVersionUpgradeAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "central-package-version-upgrade-agent";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: false);

    public async Task<AgentResult<CentralPackageVersionReport>> ExecuteAsync(
        CentralPackageVersionInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var xml = XDocument.Load(input.DirectoryPackagesPropsPath);
        var updates = new List<PackageUpdateEntry>();

        foreach (var packageVersion in xml.Descendants("PackageVersion"))
        {
            var id = packageVersion.Attribute("Include")?.Value;
            if (id is null || !input.TargetVersions.TryGetValue(id, out var targetVersion))
            {
                continue;
            }

            var versionAttr = packageVersion.Attribute("Version");
            var oldVersion = versionAttr?.Value ?? string.Empty;
            if (oldVersion == targetVersion)
            {
                continue;
            }

            if (versionAttr is not null)
            {
                versionAttr.Value = targetVersion;
            }
            else
            {
                packageVersion.SetAttributeValue("Version", targetVersion);
            }

            updates.Add(new PackageUpdateEntry(id, oldVersion, targetVersion));
        }

        if (updates.Count > 0)
        {
            xml.Save(input.DirectoryPackagesPropsPath);
        }

        var workingDirectory = Path.GetDirectoryName(input.RestoreTargetPath) ?? Directory.GetCurrentDirectory();
        var restoreRun = await _processRunner.RunAsync(
            "dotnet", $"restore \"{input.RestoreTargetPath}\"", workingDirectory, cancellationToken);

        var report = new CentralPackageVersionReport(
            updates, restoreRun.ExitCode == 0, restoreRun.StandardOutput + restoreRun.StandardError);

        context.RecordFact(AgentId, "central-package-version-report", report);

        var result = report.RestoreSucceeded
            ? AgentResult<CentralPackageVersionReport>.Create(
                report, 90, $"Updated {updates.Count} centrally-managed package version(s); restore succeeded.",
                citations: [new Citation("dotnet restore")])
            : AgentResult<CentralPackageVersionReport>.Create(
                report, 0, $"Updated {updates.Count} centrally-managed package version(s) but restore failed.");

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        CentralPackageVersionReport output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.RestoreSucceeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("Restore did not succeed after central package version updates."));
}
