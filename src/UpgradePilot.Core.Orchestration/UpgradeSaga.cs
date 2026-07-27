using MassTransit;

namespace UpgradePilot.Core.Orchestration;

/// <summary>
/// The saga referenced (but not built) in earlier commits - docs/architecture/agents.md
/// §1.1's "Saga + Blackboard" orchestration decision, implemented for real. v0.1 scope:
/// track which required agents have reported completion for a session and transition
/// to Completed once they all have, publishing UpgradeSessionCompleted. Deliberately
/// narrower than the full 17-step workflow's per-step branching/retry/approval-gate
/// logic (that needs the not-yet-built agents to have something real to orchestrate) -
/// this proves the actual mechanism (event-driven saga, not a direct call chain) works,
/// on MassTransit's in-memory transport so no broker is required for local/CLI use.
/// </summary>
public sealed class UpgradeSaga : MassTransitStateMachine<UpgradeSagaState>
{
    public State InProgress { get; private set; } = null!;

    public State Completed { get; private set; } = null!;

    public Event<StartUpgradeSession> SessionStarted { get; private set; } = null!;

    public Event<AgentStepCompleted> StepCompleted { get; private set; } = null!;

    public UpgradeSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => SessionStarted, x => x.CorrelateById(m => m.Message.SessionId));
        Event(() => StepCompleted, x => x.CorrelateById(m => m.Message.SessionId));

        Initially(
            When(SessionStarted)
                .Then(context =>
                {
                    context.Saga.RequiredAgentIds = [.. context.Message.RequiredAgentIds];
                    context.Saga.CompletedAgentIds = [];
                })
                .TransitionTo(InProgress));

        During(InProgress,
            When(StepCompleted)
                .Then(context =>
                {
                    if (!context.Saga.CompletedAgentIds.Contains(context.Message.AgentId))
                    {
                        context.Saga.CompletedAgentIds.Add(context.Message.AgentId);
                    }
                })
                .IfElse(
                    context => context.Saga.RequiredAgentIds
                        .All(required => context.Saga.CompletedAgentIds.Contains(required)),
                    finished => finished
                        .Publish(context => new UpgradeSessionCompleted(context.Saga.CorrelationId))
                        .TransitionTo(Completed),
                    stillWaiting => stillWaiting));
    }
}
