namespace UpgradePilot.Core.Domain.Agents;

/// <summary>
/// A source an agent relied on when producing a result (a RAG passage, a release note,
/// a template diff, etc.). Every <see cref="AgentResult{TOutput}"/> explanation must be
/// traceable back to citations like this — no unsourced claims.
/// </summary>
public sealed record Citation(string Source, string? Url = null);
