using System.Text.Json;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.ReleaseNotesIntelligence;

/// <summary>
/// Agent #7 (docs/architecture/agents.md §4.7): turns raw release history into a
/// breaking-change ledger. Data-fetching half is real (`gh api` against the real
/// GitHub Releases API, via IProcessRunner - reuses your already-authenticated gh
/// CLI, no token wiring needed). Classification is a keyword heuristic, not the LLM
/// summarization the full spec calls for - no LLM provider is configured in this
/// environment. Every ledger item still cites its source release URL, satisfying the
/// spec's "no unsourced claims" rule; the heuristic itself is the v0.1 limitation,
/// documented rather than hidden.
/// </summary>
public sealed class ReleaseNotesIntelligenceAgent : IUpgradePilotAgent<ReleaseNotesInput, BreakingChangeLedger>
{
    private readonly IProcessRunner _processRunner;

    public ReleaseNotesIntelligenceAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "release-notes-intelligence";
    public string Version => "0.1.0";

    /// <summary>3 attempts, exponential backoff - GitHub API rate limits, per spec §4.7.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: true);

    public async Task<AgentResult<BreakingChangeLedger>> ExecuteAsync(
        ReleaseNotesInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var run = await _processRunner.RunAsync(
            "gh", $"api \"repos/{input.Owner}/{input.Repo}/releases?per_page={input.MaxReleases}\"",
            Directory.GetCurrentDirectory(), cancellationToken);

List<BreakingChangeLedgerItem>? items = run.ExitCode == 0 ? TryParseAndClassify(run.StandardOutput) : null;

        if (items is null)
        {
            var empty = new BreakingChangeLedger([]);
            var reason = run.ExitCode != 0 ? run.StandardError.Trim() : "response was not valid JSON";
            return AgentResult<BreakingChangeLedger>.Create(
                empty, 0, $"Could not fetch releases for {input.Owner}/{input.Repo}: {reason}");
        }

        var ledger = new BreakingChangeLedger(items);

        context.RecordFact(AgentId, "breaking-change-ledger", ledger);

        var breakingCount = items.Count(i => i.Category == ChangeCategory.Breaking);
        var result = AgentResult<BreakingChangeLedger>.Create(
            ledger, 70,
            $"Classified {items.Count} change entries across {input.MaxReleases} release(s) for {input.Owner}/{input.Repo} "
                + $"({breakingCount} flagged as breaking by keyword heuristic).",
            citations: [new Citation($"github.com/{input.Owner}/{input.Repo}/releases")]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        BreakingChangeLedger output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Items.All(i => !string.IsNullOrWhiteSpace(i.SourceUrl))
            ? ValidationResult.Success()
            : ValidationResult.Failure("Every ledger item must cite a source release URL."));

    /// <summary>Null means "not parseable as the expected releases array" - distinct from a valid, empty result - so the caller can tell a real parse failure apart from "zero releases returned".</summary>
    private static List<BreakingChangeLedgerItem>? TryParseAndClassify(string releasesJson)
    {
        var items = new List<BreakingChangeLedgerItem>();

        try
        {
            using var doc = JsonDocument.Parse(releasesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "unknown" : "unknown";
                var url = release.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                var body = release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                foreach (var line in body.Split('\n'))
                {
                    var entry = line.Trim().TrimStart('-', '*', ' ');
                    if (entry.Length < 5)
                    {
                        continue;
                    }

                    items.Add(new BreakingChangeLedgerItem(tag, entry, Classify(entry), url));
                }
            }

            return items;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ChangeCategory Classify(string entry)
    {
        var lower = entry.ToLowerInvariant();

        if (lower.Contains("breaking")) return ChangeCategory.Breaking;
        if (lower.Contains("deprecat")) return ChangeCategory.Deprecation;
        if (lower.Contains("fix") || lower.Contains("bug")) return ChangeCategory.Fix;
        if (lower.Contains("feat") || lower.Contains("add") || lower.Contains("implement")) return ChangeCategory.Feature;

        return ChangeCategory.Other;
    }
}
