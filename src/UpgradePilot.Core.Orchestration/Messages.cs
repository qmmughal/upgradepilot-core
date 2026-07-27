namespace UpgradePilot.Core.Orchestration;

/// <summary>Starts an upgrade saga; declares which agent IDs must complete before the session finishes.</summary>
public sealed record StartUpgradeSession(Guid SessionId, IReadOnlyList<string> RequiredAgentIds);

/// <summary>Published when an agent finishes its step, per docs/architecture/agents.md §1.1 (command/event, not direct calls).</summary>
public sealed record AgentStepCompleted(Guid SessionId, string AgentId, int Confidence);

/// <summary>Published by the saga once every required agent has completed.</summary>
public sealed record UpgradeSessionCompleted(Guid SessionId);
