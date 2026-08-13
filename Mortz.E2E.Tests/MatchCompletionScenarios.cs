using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

public sealed class MatchCompletionScenarios
{
    /// <summary>Floor Y on the flat map, also where spawn points sit.</summary>
    private const float FLOOR_Y = 656;

    [Fact]
    public async Task AKillAtMatchPointEndsTheMatchAndEveryoneReturnsToTheLobby()
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(AKillAtMatchPointEndsTheMatchAndEveryoneReturnsToTheLobby),
            Players = ["shooter", "target"],
            Mode = null,
            RulesetPath = Path.Combine(
                RepoRoot.Path, "Mortz.E2E.Tests", "Rulesets", "first_kill_wins.toml"),
        }, TestContext.Current.CancellationToken);
        ClientDriver shooter = scenario.Client("shooter");
        ClientDriver target = scenario.Client("target");
        await scenario.ReadyAllAsync(TestContext.Current.CancellationToken);

        MatchSetupResponse setup = await scenario.Server.SetupAsync(
            TestContext.Current.CancellationToken);
        MatchConfig config = MatchConfig.FromBytes(setup.Config);
        Assert.Equal(1,
            Assert.IsType<KillsVictoryRules>(config.Rules.Victory).Target);

        await scenario.Server.PlacePlayerAsync(
            shooter.PeerId, new Vec2(400, FLOOR_Y), TestContext.Current.CancellationToken);
        await scenario.Server.PlacePlayerAsync(
            target.PeerId, new Vec2(800, FLOOR_Y), TestContext.Current.CancellationToken);
        byte aim = await scenario.AimAtAsync(shooter, target, TestContext.Current.CancellationToken);

        E2EEventCursor beforeServer = scenario.Server.Events.Cursor;
        E2EEventCursor beforeShooter = shooter.Events.Cursor;
        E2EEventCursor beforeTarget = target.Events.Cursor;
        // First 105 ticks are spawn immunity, so wait it out before firing.
        await shooter.RunPlanAsync(BotInputPlan.Sequence(
                new BotInputFrame(InputButtons.NONE, aim, ticks: 150),
                new BotInputFrame(InputButtons.FIRE, aim, ticks: 2),
                new BotInputFrame(InputButtons.NONE, aim, ticks: 300)),
            TestContext.Current.CancellationToken);

        MatchEndedEvent ended = await scenario.Server.Events.WaitAsync<MatchEndedEvent>(
            _ => true,
            scenario.Budget.MatchEvent,
            beforeServer,
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EVictorKind.PLAYER, ended.Kind);
        Assert.Equal(shooter.PeerId, ended.VictorId);

        await AssertVictorObservedAsync(scenario, shooter, beforeShooter, shooter.PeerId);
        await AssertVictorObservedAsync(scenario, target, beforeTarget, shooter.PeerId);

        // Victory lap ends, server flips to LOBBY, clients see the full roster.
        await scenario.Server.Events.WaitAsync<PhaseChangedEvent>(
            value => value.Phase == E2EPhase.LOBBY,
            scenario.Budget.MatchEvent,
            beforeServer,
            TestContext.Current.CancellationToken);
        await AssertBackInTheLobbyAsync(scenario, shooter, beforeShooter);
        await AssertBackInTheLobbyAsync(scenario, target, beforeTarget);

        // No phantom rows, nobody dropped: same two players, back in the lobby.
        ServerStateResponse lobby = await scenario.Server.StateAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EPhase.LOBBY, lobby.Phase);
        Assert.Equal(
            ["shooter", "target"],
            lobby.Players.Select(player => player.Name).Order().ToArray());
    }

    private static async Task AssertVictorObservedAsync(
        MortzScenario scenario, ClientDriver client, E2EEventCursor before, int victorId)
    {
        MatchEndObservedEvent observed = await client.Events.WaitAsync<MatchEndObservedEvent>(
            _ => true,
            scenario.Budget.MatchEvent,
            before,
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EVictorKind.PLAYER, observed.Kind);
        Assert.Equal(victorId, observed.VictorId);
    }

    private static Task AssertBackInTheLobbyAsync(
        MortzScenario scenario, ClientDriver client, E2EEventCursor before) =>
        client.Events.WaitAsync<LobbyRosterObservedEvent>(
            value => value.PlayerCount == 2,
            scenario.Budget.MatchEvent,
            before,
            TestContext.Current.CancellationToken);
}
