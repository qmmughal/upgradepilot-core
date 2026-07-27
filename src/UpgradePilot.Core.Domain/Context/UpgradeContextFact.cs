namespace UpgradePilot.Core.Domain.Context;

/// <summary>
/// A single immutable fact recorded by an agent into the session's blackboard. The
/// full set of facts for a session is the audit trail and the reproducibility record
/// for "what did we know when" — never mutated or deleted, only appended to.
/// </summary>
public sealed record UpgradeContextFact(
    Guid FactId,
    Guid SessionId,
    string AgentId,
    string FactType,
    object Payload,
    DateTimeOffset RecordedAt);
