using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Discovery.RepositoryIntelligence;

/// <summary>
/// Agent #5 (docs/architecture/agents.md §4.5): git-history-aware customization and
/// ownership signal, computed for real from `git log --numstat` (via IProcessRunner) -
/// no template baseline exists yet (that's the Template Comparator, #12, still
/// blocked), so this v0.1 produces raw churn/ownership rather than
/// template-baseline-relative customization scoring. Real signal, reduced scope,
/// documented rather than faked.
/// </summary>
public sealed class RepositoryIntelligenceAgent : IUpgradePilotAgent<string, RepositoryIntelligenceResult>
{
    private const string LogFormat = "COMMIT|%H|%an";

    private readonly IProcessRunner _processRunner;

    public RepositoryIntelligenceAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "repository-intelligence";
    public string Version => "0.1.0";

    /// <summary>2 attempts per spec §4.5; shallow-clone fallback on missing history is not yet implemented.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 2, InitialDelay: TimeSpan.FromSeconds(1), UseExponentialBackoff: false);

    public async Task<AgentResult<RepositoryIntelligenceResult>> ExecuteAsync(
        string repositoryPath, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var run = await _processRunner.RunAsync(
            "git", $"log --numstat --format=\"{LogFormat}\"", repositoryPath, cancellationToken);

        if (run.ExitCode != 0)
        {
            var empty = new RepositoryIntelligenceResult(new CustomizationHeatmap([]), new OwnershipMap([]));
            return AgentResult<RepositoryIntelligenceResult>.Create(
                empty, 0, $"Could not read git history: {run.StandardError.Trim()}");
        }

        var (heatmap, ownership) = ParseLog(run.StandardOutput);
        var intelligence = new RepositoryIntelligenceResult(heatmap, ownership);

        context.RecordFact(AgentId, "customization-heatmap", heatmap);
        context.RecordFact(AgentId, "ownership-map", ownership);

        var result = AgentResult<RepositoryIntelligenceResult>.Create(
            intelligence,
            confidence: 80,
            explanation: $"Analyzed churn and ownership for {heatmap.Churn.Count} file(s) across git history. "
                + "Template-baseline-relative customization scoring requires the Template Comparator (#12), not yet built.",
            citations: [new Citation("git log --numstat")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        RepositoryIntelligenceResult output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidationResult.Success());

    private static (CustomizationHeatmap Heatmap, OwnershipMap Ownership) ParseLog(string log)
    {
        var churnByFile = new Dictionary<string, (int Commits, int Insertions, int Deletions)>();
        var authorCommitsByFile = new Dictionary<string, Dictionary<string, int>>();

        string? currentAuthor = null;
        var seenFilesInCurrentCommit = new HashSet<string>();

        foreach (var rawLine in log.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("COMMIT|", StringComparison.Ordinal))
            {
                var parts = line.Split('|', 3);
                currentAuthor = parts.Length >= 3 ? parts[2] : "unknown";
                seenFilesInCurrentCommit = [];
                continue;
            }

            if (string.IsNullOrWhiteSpace(line) || currentAuthor is null)
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != 3)
            {
                continue;
            }

            var path = fields[2];
            _ = int.TryParse(fields[0], out var insertions);
            _ = int.TryParse(fields[1], out var deletions);

            var isFirstTimeThisCommit = seenFilesInCurrentCommit.Add(path);

            var churn = churnByFile.GetValueOrDefault(path);
            churnByFile[path] = (
                churn.Commits + (isFirstTimeThisCommit ? 1 : 0),
                churn.Insertions + insertions,
                churn.Deletions + deletions);

            if (!authorCommitsByFile.TryGetValue(path, out var authorCounts))
            {
                authorCounts = [];
                authorCommitsByFile[path] = authorCounts;
            }

            if (isFirstTimeThisCommit)
            {
                authorCounts[currentAuthor] = authorCounts.GetValueOrDefault(currentAuthor) + 1;
            }
        }

        var churnList = churnByFile
            .Select(kv => new FileChurn(kv.Key, kv.Value.Commits, kv.Value.Insertions, kv.Value.Deletions))
            .ToList();

        var ownershipList = authorCommitsByFile
            .Select(kv =>
            {
                var top = kv.Value.OrderByDescending(a => a.Value).First();
                return new FileOwnership(kv.Key, top.Key, top.Value);
            })
            .ToList();

        return (new CustomizationHeatmap(churnList), new OwnershipMap(ownershipList));
    }
}
