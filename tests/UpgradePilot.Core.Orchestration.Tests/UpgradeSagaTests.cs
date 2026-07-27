using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace UpgradePilot.Core.Orchestration.Tests;

public class UpgradeSagaTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddSagaStateMachine<UpgradeSaga, UpgradeSagaState>())
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Saga_TransitionsToInProgress_WhenStarted()
    {
        var sessionId = Guid.NewGuid();
        var sagaHarness = _harness.GetSagaStateMachineHarness<UpgradeSaga, UpgradeSagaState>();

        await _harness.Bus.Publish(new StartUpgradeSession(sessionId, ["repository-analyzer", "version-detector"]));

        Assert.True(await sagaHarness.Consumed.Any<StartUpgradeSession>());
        Assert.NotNull(await sagaHarness.Exists(sessionId, x => x.InProgress));
    }

    [Fact]
    public async Task Saga_StaysInProgress_UntilAllRequiredAgentsComplete()
    {
        var sessionId = Guid.NewGuid();
        var sagaHarness = _harness.GetSagaStateMachineHarness<UpgradeSaga, UpgradeSagaState>();

        await _harness.Bus.Publish(new StartUpgradeSession(sessionId, ["repository-analyzer", "version-detector"]));
        await sagaHarness.Exists(sessionId, x => x.InProgress);

        await _harness.Bus.Publish(new AgentStepCompleted(sessionId, "repository-analyzer", 100));

        Assert.NotNull(await sagaHarness.Exists(sessionId, x => x.InProgress));
        Assert.False(await _harness.Published.Any<UpgradeSessionCompleted>());
    }

    [Fact]
    public async Task Saga_CompletesAndPublishesEvent_WhenAllRequiredAgentsReport()
    {
        var sessionId = Guid.NewGuid();
        var sagaHarness = _harness.GetSagaStateMachineHarness<UpgradeSaga, UpgradeSagaState>();

        await _harness.Bus.Publish(new StartUpgradeSession(sessionId, ["repository-analyzer", "version-detector"]));
        await sagaHarness.Exists(sessionId, x => x.InProgress);

        await _harness.Bus.Publish(new AgentStepCompleted(sessionId, "repository-analyzer", 100));
        await _harness.Bus.Publish(new AgentStepCompleted(sessionId, "version-detector", 90));

        Assert.NotNull(await sagaHarness.Exists(sessionId, x => x.Completed));
        Assert.True(await _harness.Published.Any<UpgradeSessionCompleted>(m => m.Context?.Message.SessionId == sessionId));
    }

    [Fact]
    public async Task Saga_IgnoresDuplicateAgentCompletion_WithoutCompletingEarly()
    {
        var sessionId = Guid.NewGuid();
        var sagaHarness = _harness.GetSagaStateMachineHarness<UpgradeSaga, UpgradeSagaState>();

        await _harness.Bus.Publish(new StartUpgradeSession(sessionId, ["repository-analyzer", "version-detector"]));
        await sagaHarness.Exists(sessionId, x => x.InProgress);

        await _harness.Bus.Publish(new AgentStepCompleted(sessionId, "repository-analyzer", 100));
        await _harness.Bus.Publish(new AgentStepCompleted(sessionId, "repository-analyzer", 100));

        Assert.NotNull(await sagaHarness.Exists(sessionId, x => x.InProgress));
    }
}
