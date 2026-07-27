namespace UpgradePilot.Core.Domain.Context;

/// <summary>
/// The persistence seam for <see cref="UpgradeContext"/>, per
/// docs/architecture/open-core-boundary.md §3: OSS/local mode (upgradepilot-cli)
/// implements this against local storage (SQLite); UpgradePilot Cloud implements it
/// against a multi-tenant Postgres cluster. Same port, swappable adapter - Domain
/// stays free of any concrete storage dependency.
/// </summary>
public interface IUpgradeContextStore
{
    Task SaveFactAsync(UpgradeContextFact fact, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UpgradeContextFact>> LoadFactsAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
