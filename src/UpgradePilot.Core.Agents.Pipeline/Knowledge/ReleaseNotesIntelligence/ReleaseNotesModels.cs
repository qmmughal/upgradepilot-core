namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.ReleaseNotesIntelligence;

public sealed record ReleaseNotesInput(string Owner, string Repo, int MaxReleases = 10);

public enum ChangeCategory
{
    Other,
    Breaking,
    Deprecation,
    Feature,
    Fix,
}

public sealed record BreakingChangeLedgerItem(string ReleaseTag, string Description, ChangeCategory Category, string SourceUrl);

public sealed record BreakingChangeLedger(IReadOnlyList<BreakingChangeLedgerItem> Items);
