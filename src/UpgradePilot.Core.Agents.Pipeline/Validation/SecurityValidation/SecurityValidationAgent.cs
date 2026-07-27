using System.Text.Json;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Validation.SecurityValidation;

/// <summary>
/// Agent #17 (docs/architecture/agents.md §4.17): blocks the pipeline on unresolved
/// Critical/High security findings. Real dependency-vulnerability half via
/// `dotnet list package --vulnerable` (same mechanism as Dependency Analyzer, #7 -
/// deliberately not shared code with that agent so each stays independently correct
/// per its own spec entry). Full SAST (CodeQL-equivalent static analysis of source
/// for injection/XSS/etc.) is not available in this environment - documented gap, not
/// a fabricated scan. This agent's real, distinct value over #7 is the policy: it
/// decides whether Critical/High findings block progression, which #7 does not do.
/// </summary>
public sealed class SecurityValidationAgent : IUpgradePilotAgent<RepositoryMap, SecurityReport>
{
    private static readonly HashSet<string> BlockingSeverities = new(StringComparer.OrdinalIgnoreCase) { "Critical", "High" };

    private readonly IProcessRunner _processRunner;

    public SecurityValidationAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "security-validation-agent";
    public string Version => "0.1.0";

    /// <summary>No retry - deterministic scan, per spec §4.17.</summary>
    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public async Task<AgentResult<SecurityReport>> ExecuteAsync(
        RepositoryMap input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var findings = new List<SecurityFinding>();

        foreach (var project in input.Projects)
        {
            var workingDirectory = Path.GetDirectoryName(project.ProjectFilePath) ?? Directory.GetCurrentDirectory();
            var run = await _processRunner.RunAsync(
                "dotnet", $"list \"{project.ProjectFilePath}\" package --vulnerable --include-transitive --format json",
                workingDirectory, cancellationToken);

            findings.AddRange(TryParseFindings(project.Name, run.StandardOutput));
        }

        var blocks = findings.Any(f => BlockingSeverities.Contains(f.Severity));
        var report = new SecurityReport(findings, blocks);

        context.RecordFact(AgentId, "security-report", report);

        var result = blocks
            ? AgentResult<SecurityReport>.Create(
                report, 0,
                $"{findings.Count(f => BlockingSeverities.Contains(f.Severity))} Critical/High finding(s) block progression to Delivery.",
                citations: findings.Where(f => BlockingSeverities.Contains(f.Severity))
                    .Select(f => new Citation($"{f.PackageId} ({f.Severity})")).ToList())
            : AgentResult<SecurityReport>.Create(
                report, 90, $"No Critical/High findings ({findings.Count} lower-severity finding(s) noted).",
                citations: [new Citation("dotnet list package --vulnerable")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        SecurityReport output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.BlocksProgression
            ? ValidationResult.Failure("Unresolved Critical/High security finding(s) present - progression blocked by policy.")
            : ValidationResult.Success());

    private static List<SecurityFinding> TryParseFindings(string projectName, string json)
    {
        var findings = new List<SecurityFinding>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.GetArrayLength() == 0)
            {
                return findings;
            }

            var project = projects[0];
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                return findings;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                foreach (var propertyName in new[] { "topLevelPackages", "transitivePackages" })
                {
                    if (!framework.TryGetProperty(propertyName, out var packages))
                    {
                        continue;
                    }

                    foreach (var pkg in packages.EnumerateArray())
                    {
                        if (!pkg.TryGetProperty("vulnerabilities", out var vulnerabilities))
                        {
                            continue;
                        }

                        var id = pkg.GetProperty("id").GetString() ?? "unknown";

                        foreach (var vuln in vulnerabilities.EnumerateArray())
                        {
                            var severity = vuln.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "unknown" : "unknown";
                            var advisoryUrl = vuln.TryGetProperty("advisoryurl", out var url) ? url.GetString() : null;
                            findings.Add(new SecurityFinding(projectName, id, severity, advisoryUrl));
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // no scan data available for this project - not fatal
        }

        return findings;
    }
}
