using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

public sealed class ConvergenceScenarios
{
    /// <summary>Ground level on the flat map; spawn points sit here too.</summary>
    private const float FLOOR_Y = 656;

    /// <summary>Half a body's slack. The assertion is about replication, not a
    /// pixel-exact landing.</summary>
    private const float TOLERANCE = 24f;

    [Fact]
    public async Task PlacedPlayersConvergeOnBothClients()
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(PlacedPlayersConvergeOnBothClients),
            Players = ["alice", "bob"],
        }, TestContext.Current.CancellationToken);
        ClientDriver alice = scenario.Client("alice");
        ClientDriver bob = scenario.Client("bob");
        await scenario.ReadyAllAsync(TestContext.Current.CancellationToken);

        // Real coordinates on the flat map's uniform floor, well away from the
        // spawn points.
        Vec2 alicePos = new(300, FLOOR_Y);
        Vec2 bobPos = new(900, FLOOR_Y);
        PlayerPlacedResponse placedAlice = await scenario.Server.PlacePlayerAsync(
            alice.PeerId, alicePos, TestContext.Current.CancellationToken);
        PlayerPlacedResponse placedBob = await scenario.Server.PlacePlayerAsync(
            bob.PeerId, bobPos, TestContext.Current.CancellationToken);
        int appliedTick = Math.Max(placedAlice.AppliedTick, placedBob.AppliedTick);

        foreach (ClientDriver observer in new[] { alice, bob })
        {
            SnapshotObservedEvent snapshot = await observer.Events.WaitAsync<SnapshotObservedEvent>(
                e => e.Tick >= appliedTick && e.Players.Length == 2,
                scenario.Budget.MatchEvent,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains(snapshot.Players, p => p.PeerId == alice.PeerId
                                                   && (p.Position - alicePos).Length() < TOLERANCE);
            Assert.Contains(snapshot.Players, p => p.PeerId == bob.PeerId
                                                   && (p.Position - bobPos).Length() < TOLERANCE);
        }
    }

    [Fact]
    public async Task OneClientShootsAnotherAndTheKillReplicates()
    {
        // Migrates --test-hunt. The old hook lobbed shells with hand-rolled
        // ballistics and hoped. AimAtAsync asks the sim, aims once, lands.
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(OneClientShootsAnotherAndTheKillReplicates),
            Players = ["shooter", "target"],
        }, TestContext.Current.CancellationToken);
        ClientDriver shooter = scenario.Client("shooter");
        ClientDriver target = scenario.Client("target");
        await scenario.ReadyAllAsync(TestContext.Current.CancellationToken);

        // Well inside the mortar's range, on the flat map's open floor.
        await scenario.Server.PlacePlayerAsync(
            shooter.PeerId, new Vec2(400, FLOOR_Y), TestContext.Current.CancellationToken);
        await scenario.Server.PlacePlayerAsync(
            target.PeerId, new Vec2(800, FLOOR_Y), TestContext.Current.CancellationToken);
        byte aim = await scenario.AimAtAsync(shooter, target, TestContext.Current.CancellationToken);

        E2EEventCursor beforeServer = scenario.Server.Events.Cursor;
        E2EEventCursor beforeTarget = target.Events.Cursor;
        await shooter.RunPlanAsync(BotInputPlan.Sequence(
                new BotInputFrame(InputButtons.NONE, aim, ticks: 150),
                new BotInputFrame(InputButtons.FIRE, aim, ticks: 2),
                new BotInputFrame(InputButtons.NONE, aim, ticks: 300)),
            TestContext.Current.CancellationToken);

        EliminationEvent kill = await scenario.Server.Events.WaitAsync<EliminationEvent>(
            e => e.VictimId == target.PeerId,
            scenario.Budget.MatchEvent,
            beforeServer,
            TestContext.Current.CancellationToken);
        Assert.Equal(shooter.PeerId, kill.KillerId);
        Assert.False(kill.Suicide);

        // The victim's own client sees the kill it just took.
        await target.Events.WaitAsync<EliminationObservedEvent>(
            e => e.VictimId == target.PeerId && e.KillerId == shooter.PeerId,
            scenario.Budget.MatchEvent,
            beforeTarget,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FiringIntoTheFloorSelfKillsAndReplicates()
    {
        // Migrates --test-fire. Aim 64 is straight down. Holding FIRE at point
        // blank exercises the explosion and the self-death.
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(FiringIntoTheFloorSelfKillsAndReplicates),
            Players = ["shooter", "witness"],
        }, TestContext.Current.CancellationToken);
        ClientDriver shooter = scenario.Client("shooter");
        ClientDriver witness = scenario.Client("witness");
        await scenario.ReadyAllAsync(TestContext.Current.CancellationToken);
        E2EEventCursor beforeServer = scenario.Server.Events.Cursor;
        E2EEventCursor beforeWitness = witness.Events.Cursor;

        // The first 105 ticks are spawn immunity, during which CanFire refuses;
        // the plan waits it out before holding FIRE.
        await shooter.RunPlanAsync(BotInputPlan.Sequence(
                new BotInputFrame(InputButtons.NONE, aim: 64, ticks: 150),
                new BotInputFrame(InputButtons.FIRE, aim: 64, ticks: 90),
                new BotInputFrame(InputButtons.NONE, aim: 64, ticks: 600)),
            TestContext.Current.CancellationToken);

        EliminationEvent kill = await scenario.Server.Events.WaitAsync<EliminationEvent>(
            e => e.VictimId == shooter.PeerId && e.Suicide,
            scenario.Budget.MatchEvent,
            beforeServer,
            TestContext.Current.CancellationToken);

        // Use the cursor, not the tick, to make this non-vacuous. Elimination
        // rides a reliable message, snapshots ride an unreliable one - the
        // witness's last applied snapshot can still be the tick before the kill.
        EliminationObservedEvent witnessed = await witness.Events
            .WaitAsync<EliminationObservedEvent>(
                e => e.VictimId == shooter.PeerId && e.Suicide,
                scenario.Budget.MatchEvent,
                beforeWitness,
                TestContext.Current.CancellationToken);
        Assert.Equal(kill.KillerId, witnessed.KillerId);
    }
}
