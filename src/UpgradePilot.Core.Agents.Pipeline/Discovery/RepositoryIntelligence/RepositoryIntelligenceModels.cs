namespace UpgradePilot.Core.Agents.Pipeline.Discovery.RepositoryIntelligence;

public sealed record FileChurn(string FilePath, int CommitCount, int TotalInsertions, int TotalDeletions);

public sealed record CustomizationHeatmap(IReadOnlyList<FileChurn> Churn);

public sealed record FileOwnership(string FilePath, string PrimaryAuthor, int CommitsByPrimaryAuthor);

public sealed record OwnershipMap(IReadOnlyList<FileOwnership> Ownership);

public sealed record RepositoryIntelligenceResult(CustomizationHeatmap Heatmap, OwnershipMap Ownership);
