namespace UpgradePilot.Core.Domain.Context;

/// <summary>
/// The shared blackboard for one upgrade session. Agents don't call each other or pass
/// large payloads through messages — they read prior facts here and append their own.
/// Append-only by design: this is what lets the same object serve as working memory,
/// audit trail, and rollback/explainability record simultaneously.
/// </summary>
public sealed class UpgradeContext
{
    private readonly List<UpgradeContextFact> _facts = [];

    public UpgradeContext(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id must not be empty.", nameof(sessionId));
        }

        SessionId = sessionId;
    }

    /// <summary>
    /// Rehydrates a context from previously persisted facts (see IUpgradeContextStore).
    /// Facts are appended in the order given - callers should load them ordered by
    /// RecordedAt to preserve the append-only audit trail's meaning.
    /// </summary>
    public static UpgradeContext Restore(Guid sessionId, IEnumerable<UpgradeContextFact> facts)
    {
        var context = new UpgradeContext(sessionId);
        context._facts.AddRange(facts.Where(f => f.SessionId == sessionId));
        return context;
    }

    public Guid SessionId { get; }

    public IReadOnlyList<UpgradeContextFact> Facts => _facts.AsReadOnly();

    public UpgradeContextFact RecordFact(string agentId, string factType, object payload)
    {
        var fact = new UpgradeContextFact(
            FactId: Guid.NewGuid(),
            SessionId: SessionId,
            AgentId: agentId,
            FactType: factType,
            Payload: payload,
            RecordedAt: DateTimeOffset.UtcNow);

        _facts.Add(fact);
        return fact;
    }

    public UpgradeContextFact? LatestFact(string factType) =>
        _facts.Where(f => f.FactType == factType)
              .OrderByDescending(f => f.RecordedAt)
              .FirstOrDefault();

    public IEnumerable<UpgradeContextFact> FactsFrom(string agentId) =>
        _facts.Where(f => f.AgentId == agentId);
}
