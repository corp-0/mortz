using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

/// <summary>Real ENet and real Godot scene lifecycle coverage for the
/// server-to-client readiness.</summary>
public sealed class ReadinessScenarios
{
    [Fact]
    public async Task SlowScreenLoadDoesNotDropMessagesOrStartTheMatchEarly()
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(SlowScreenLoadDoesNotDropMessagesOrStartTheMatchEarly),
            Players = ["fast", "slow"],
            SlowScreenPlayer = "slow",
            ScreenLoadDelayMs = 4_000,
        }, TestContext.Current.CancellationToken);
        ClientDriver fast = scenario.Client("fast");
        ClientDriver slow = scenario.Client("slow");

        // The delayed lobby attachment covers the original join-chat race.
        AssertNoMissingHandler(fast);
        AssertNoMissingHandler(slow);

        E2EEventCursor beforeReady = scenario.Server.Events.Cursor;
        await fast.SetReadyAsync(true, TestContext.Current.CancellationToken);
        await slow.SetReadyAsync(true, TestContext.Current.CancellationToken);
        await scenario.Server.Events.WaitAsync<PhaseChangedEvent>(
            value => value.Phase == E2EPhase.MATCH,
            scenario.Budget.MatchEvent,
            beforeReady,
            TestContext.Current.CancellationToken);

        await fast.Events.WaitAsync<MatchEnteredEvent>(
            _ => true,
            scenario.Budget.MatchEvent,
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            scenario.Server.Events.WaitAsync<MatchTickEvent>(
                _ => true,
                TimeSpan.FromSeconds(2) * E2EBudget.EnvironmentScale,
                beforeReady,
                TestContext.Current.CancellationToken));

        await slow.Events.WaitAsync<MatchEnteredEvent>(
            _ => true,
            scenario.Budget.MatchEvent,
            cancellationToken: TestContext.Current.CancellationToken);
        MatchTickEvent firstTick = await scenario.Server.Events.WaitAsync<MatchTickEvent>(
            _ => true,
            scenario.Budget.MatchEvent,
            beforeReady,
            TestContext.Current.CancellationToken);
        Assert.Equal(60, firstTick.Tick);

        await fast.Events.WaitAsync<SnapshotObservedEvent>(
            value => value.Tick >= firstTick.Tick,
            scenario.Budget.MatchEvent,
            cancellationToken: TestContext.Current.CancellationToken);
        await slow.Events.WaitAsync<SnapshotObservedEvent>(
            value => value.Tick >= firstTick.Tick,
            scenario.Budget.MatchEvent,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertNoMissingHandler(fast);
        AssertNoMissingHandler(slow);
    }

    private static void AssertNoMissingHandler(ClientDriver client) =>
        Assert.False(client.Process.LogContains("no handler for message id"),
            $"{client.Name} dropped a message:{Environment.NewLine}{client.Process.Report()}");
}
