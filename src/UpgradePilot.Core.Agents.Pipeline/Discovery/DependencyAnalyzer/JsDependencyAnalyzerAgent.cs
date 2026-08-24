using System.Text.Json;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;

/// <summary>
/// The npm counterpart to <see cref="DependencyAnalyzerAgent"/> - real `npm outdated
/// --json` against the npm registry, via IProcessRunner. npm's own documented behavior
/// is to exit 1 whenever any outdated package is found (not an error condition, despite
/// the non-zero code) - the real signal this agent uses for success/failure is whether
/// the output actually parses as JSON, same "parse success is the real signal, not the
/// exit code" pattern <see cref="DependencyAnalyzerAgent"/> already uses for
/// `dotnet list package --vulnerable`.
/// </summary>
public sealed class JsDependencyAnalyzerAgent : IUpgradePilotAgent<string, JsDependencyAnalysisResult>
{
    private readonly IProcessRunner _processRunner;

    public JsDependencyAnalyzerAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "js-dependency-analyzer";
    public string Version => "0.1.0";

    /// <summary>2 attempts on registry timeouts, matching the NuGet-facing agent's policy.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: false);

    public async Task<AgentResult<JsDependencyAnalysisResult>> ExecuteAsync(
        string projectDirectory, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var run = await _processRunner.RunAsync("npm", "outdated --json", projectDirectory, cancellationToken);
        var outdated = TryParseOutdated(run.StandardOutput);

        if (outdated is null)
        {
            var failed = new JsDependencyAnalysisResult([]);
            return AgentResult<JsDependencyAnalysisResult>.Create(
                failed, 0, $"Could not parse `npm outdated` output: {run.StandardError.Trim()}");
        }

        var result = new JsDependencyAnalysisResult(outdated);
        context.RecordFact(AgentId, "js-dependency-analysis", result);

        return AgentResult<JsDependencyAnalysisResult>.Create(
            result, 90, $"Found {outdated.Count} outdated package(s).", citations: [new Citation("npm outdated")]);
    }

    public Task<ValidationResult> ValidateAsync(
        JsDependencyAnalysisResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidationResult.Success());

    private static List<JsPackageDependency>? TryParseOutdated(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var results = new List<JsPackageDependency>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var current = property.Value.TryGetProperty("current", out var c) ? c.GetString() : null;
                var latest = property.Value.TryGetProperty("latest", out var l) ? l.GetString() : null;

                if (current is not null && latest is not null)
                {
                    results.Add(new JsPackageDependency(property.Name, current, latest));
                }
            }

            return results;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
