using System.Xml.Linq;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

/// <summary>
/// Bumps a project's `&lt;TargetFramework&gt;` (e.g. net6.0 -> net8.0), the other half of
/// a real .NET upgrade that <see cref="PackageUpgradeAgent"/> deliberately doesn't do -
/// package version and TFM are independent axes and either can need bumping without the
/// other. v0.1 scope: single `&lt;TargetFramework&gt;` only, not multi-target
/// `&lt;TargetFrameworks&gt;` (plural) projects - documented gap, flagged via
/// <see cref="ValidateAsync"/> rather than silently mishandled.
/// </summary>
public sealed class DotNetTargetFrameworkUpgradeAgent : IUpgradePilotAgent<DotNetTargetFrameworkInput, DotNetTargetFrameworkReport>
{
    private readonly IProcessRunner _processRunner;

    public DotNetTargetFrameworkUpgradeAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "dotnet-target-framework-upgrade-agent";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: false);

    public async Task<AgentResult<DotNetTargetFrameworkReport>> ExecuteAsync(
        DotNetTargetFrameworkInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var xml = XDocument.Load(input.ProjectFilePath);
        var element = xml.Descendants("TargetFramework").FirstOrDefault();
        var oldTargetFramework = element?.Value;
        var changed = element is not null && oldTargetFramework != input.TargetFramework;

        if (changed)
        {
            element!.Value = input.TargetFramework;
            xml.Save(input.ProjectFilePath);
        }

        var workingDirectory = Path.GetDirectoryName(input.ProjectFilePath) ?? Directory.GetCurrentDirectory();
        var restoreRun = await _processRunner.RunAsync(
            "dotnet", $"restore \"{input.ProjectFilePath}\"", workingDirectory, cancellationToken);

        var report = new DotNetTargetFrameworkReport(
            input.ProjectFilePath, oldTargetFramework, input.TargetFramework, changed,
            restoreRun.ExitCode == 0, restoreRun.StandardOutput + restoreRun.StandardError);

        context.RecordFact(AgentId, "target-framework-report", report);

        var result = element is null
            ? AgentResult<DotNetTargetFrameworkReport>.Create(
                report, 0, "Project uses <TargetFrameworks> (multi-target) or has no <TargetFramework> element - not supported in v0.1.")
            : report.RestoreSucceeded
                ? AgentResult<DotNetTargetFrameworkReport>.Create(
                    report, 90, $"Target framework {(changed ? $"changed {oldTargetFramework} -> {input.TargetFramework}" : "already at target")}; restore succeeded.",
                    citations: [new Citation("dotnet restore")])
                : AgentResult<DotNetTargetFrameworkReport>.Create(
                    report, 0, $"Target framework set to {input.TargetFramework} but restore failed.");

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        DotNetTargetFrameworkReport output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.RestoreSucceeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("Restore did not succeed after the target framework change."));
}
