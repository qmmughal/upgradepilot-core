using System.Xml.Linq;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

/// <summary>
/// Agent #12 (docs/architecture/agents.md §4.12): executes package version bumps per
/// the upgrade plan. Real XML edit of PackageReference Version attributes, then a
/// real `dotnet restore` (against nuget.org) to verify the bump actually resolves -
/// not just a text substitution that looks right.
/// </summary>
public sealed class PackageUpgradeAgent : IUpgradePilotAgent<PackageUpgradeInput, PackageUpgradeReport>
{
    private readonly IProcessRunner _processRunner;

    public PackageUpgradeAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "package-upgrade-agent";
    public string Version => "0.1.0";

    /// <summary>2 attempts on registry timeouts per spec §4.12; a real version conflict is not retried.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: false);

    public async Task<AgentResult<PackageUpgradeReport>> ExecuteAsync(
        PackageUpgradeInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var xml = XDocument.Load(input.ProjectFilePath);
        var updates = new List<PackageUpdateEntry>();

        foreach (var packageRef in xml.Descendants("PackageReference"))
        {
            var id = packageRef.Attribute("Include")?.Value;
            if (id is null || !input.TargetVersions.TryGetValue(id, out var targetVersion))
            {
                continue;
            }

            var versionAttr = packageRef.Attribute("Version");
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
                packageRef.SetAttributeValue("Version", targetVersion);
            }

            updates.Add(new PackageUpdateEntry(id, oldVersion, targetVersion));
        }

        if (updates.Count > 0)
        {
            xml.Save(input.ProjectFilePath);
        }

        var workingDirectory = Path.GetDirectoryName(input.ProjectFilePath) ?? Directory.GetCurrentDirectory();
        var restoreRun = await _processRunner.RunAsync(
            "dotnet", $"restore \"{input.ProjectFilePath}\"", workingDirectory, cancellationToken);

        var report = new PackageUpgradeReport(
            updates, restoreRun.ExitCode == 0, restoreRun.StandardOutput + restoreRun.StandardError);

        context.RecordFact(AgentId, "package-upgrade-report", report);

        var result = report.RestoreSucceeded
            ? AgentResult<PackageUpgradeReport>.Create(
                report, 90, $"Updated {updates.Count} package reference(s); restore succeeded.",
                citations: [new Citation("dotnet restore")])
            : AgentResult<PackageUpgradeReport>.Create(
                report, 0, $"Updated {updates.Count} package reference(s) but restore failed.");

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        PackageUpgradeReport output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.RestoreSucceeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("Restore did not succeed after package updates."));
}
