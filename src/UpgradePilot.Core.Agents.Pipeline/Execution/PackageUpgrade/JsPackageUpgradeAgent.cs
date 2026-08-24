using System.Text.Json;
using System.Text.Json.Nodes;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;

/// <summary>
/// The npm counterpart to <see cref="PackageUpgradeAgent"/> - same discipline: a real
/// edit of package.json's "dependencies"/"devDependencies" entries, then a real
/// `npm install` (against the npm registry) to verify the bump actually resolves, not
/// just a text substitution that looks right. Backs the React and Next.js stack
/// adapters' package-upgrade step.
/// </summary>
public sealed class JsPackageUpgradeAgent : IUpgradePilotAgent<JsPackageUpgradeInput, JsPackageUpgradeReport>
{
    private const string DependenciesKey = "dependencies";
    private const string DevDependenciesKey = "devDependencies";

    private readonly IProcessRunner _processRunner;

    public JsPackageUpgradeAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "js-package-upgrade-agent";
    public string Version => "0.1.0";

    /// <summary>2 attempts on registry timeouts, matching <see cref="PackageUpgradeAgent"/>'s NuGet policy.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: false);

    public async Task<AgentResult<JsPackageUpgradeReport>> ExecuteAsync(
        JsPackageUpgradeInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(input.PackageJsonPath, cancellationToken))?.AsObject()
            ?? throw new InvalidOperationException($"'{input.PackageJsonPath}' is not a valid JSON object.");

        var updates = new List<JsPackageUpdateEntry>();

        foreach (var sectionKey in new[] { DependenciesKey, DevDependenciesKey })
        {
            if (root[sectionKey] is not JsonObject section)
            {
                continue;
            }

            foreach (var packageId in section.Select(kvp => kvp.Key).ToList())
            {
                if (!input.TargetVersions.TryGetValue(packageId, out var targetVersion))
                {
                    continue;
                }

                var oldVersion = section[packageId]?.GetValue<string>() ?? string.Empty;
                var newVersionSpecifier = ApplyExistingRangePrefix(oldVersion, targetVersion);
                if (oldVersion == newVersionSpecifier)
                {
                    continue;
                }

                section[packageId] = newVersionSpecifier;
                updates.Add(new JsPackageUpdateEntry(packageId, oldVersion, newVersionSpecifier, sectionKey == DevDependenciesKey));
            }
        }

        if (updates.Count > 0)
        {
            await File.WriteAllTextAsync(
                input.PackageJsonPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        var workingDirectory = Path.GetDirectoryName(input.PackageJsonPath) ?? Directory.GetCurrentDirectory();
        var installRun = await _processRunner.RunAsync("npm", "install", workingDirectory, cancellationToken);

        var report = new JsPackageUpgradeReport(
            updates, installRun.ExitCode == 0, installRun.StandardOutput + installRun.StandardError);

        context.RecordFact(AgentId, "js-package-upgrade-report", report);

        var result = report.InstallSucceeded
            ? AgentResult<JsPackageUpgradeReport>.Create(
                report, 90, $"Updated {updates.Count} package.json entr{(updates.Count == 1 ? "y" : "ies")}; npm install succeeded.",
                citations: [new Citation("npm install")])
            : AgentResult<JsPackageUpgradeReport>.Create(
                report, 0, $"Updated {updates.Count} package.json entr{(updates.Count == 1 ? "y" : "ies")} but npm install failed.");

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        JsPackageUpgradeReport output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.InstallSucceeded
            ? ValidationResult.Success()
            : ValidationResult.Failure("npm install did not succeed after package.json updates."));

    /// <summary>package.json convention: a caret/tilde range prefix on the old specifier carries forward onto the new version rather than being dropped.</summary>
    private static string ApplyExistingRangePrefix(string oldVersion, string targetVersion)
    {
        if (targetVersion.Length > 0 && (targetVersion[0] == '^' || targetVersion[0] == '~'))
        {
            return targetVersion;
        }

        if (oldVersion.Length > 0 && (oldVersion[0] == '^' || oldVersion[0] == '~'))
        {
            return oldVersion[0] + targetVersion;
        }

        return targetVersion;
    }
}
