using MassTransit;

namespace UpgradePilot.Core.Orchestration;

/// <summary>
/// Persisted saga instance state. In-memory by default in this repo (no saga
/// repository configured) - a real deployment would plug in a saga persistence
/// provider (MassTransit supports many, including EF Core/Postgres for
/// UpgradePilot Cloud); that's an infrastructure choice, not a saga-design one, so it's
/// left unconfigured here rather than faked.
/// </summary>
public sealed class UpgradeSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = default!;

    public List<string> RequiredAgentIds { get; set; } = [];

    public List<string> CompletedAgentIds { get; set; } = [];
}
