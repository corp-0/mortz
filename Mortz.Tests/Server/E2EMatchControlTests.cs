using Mortz.Core.Match.Configuration;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Core.Sim;
using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Server;

/// <summary>The mutation seam driven through a whole GameServer: real lobby,
/// real match phase, real scoring. Nothing here touches an engine or a socket.</summary>
public class E2EMatchControlTests : IDisposable
{
    private readonly E2EMatchControl _control = new();
    private readonly RecordingObserver _observer = new();
    private readonly TestServer _server;

    public E2EMatchControlTests()
    {
        _server = new TestServer(rules: new MatchConfig
        {
            Rules = new ModeRules { SpawnImmunity = 0 },
        }, observer: _observer, control: _control);
    }

    public void Dispose() => _server.Dispose();

    [Fact]
    public void NothingIsAppliedUntilATickRuns()
    {
        StartMatch();
        MutationOutcome? outcome = null;

        _control.PlacePlayer(7, new Vec2(64, 32), value => outcome = value);

        Assert.Null(outcome);
    }

    [Fact]
    public void PlacementCompletesWithTheTickThatAlreadyReflectsIt()
    {
        StartMatch();
        MutationOutcome outcome = default;
        _control.PlacePlayer(7, new Vec2(20, 40), value => outcome = value);

        _server.Tick();

        Assert.Null(outcome.Error);
        // The world has stepped once past the placement, so the tick the caller
        // is told about is one the snapshot for that tick already includes.
        Assert.Equal(_observer.LastUpdate!.Tick, outcome.AppliedTick);
    }

    [Fact]
    public void PlacementMovesThePlayerBeforeTheTickSimulates()
    {
        StartMatch();
        Vec2 target = new(20, 40);
        _control.PlacePlayer(7, target, _ => { });

        _server.Tick();
        WorldStateOutcome state = ReadState();

        E2EWorldPlayer placed = Assert.Single(state.Players, player => player.PeerId == 7);
        Assert.Equal(target.X, placed.Position.X);
    }

    [Fact]
    public void PlacementForAnUnknownPeerReportsAnErrorAndNeverThrows()
    {
        StartMatch();
        MutationOutcome outcome = default;
        _control.PlacePlayer(999, new Vec2(20, 40), value => outcome = value);

        _server.Tick();

        Assert.NotNull(outcome.Error);
        Assert.Contains("999", outcome.Error);
    }

    [Fact]
    public void DamageCompletesWithThePostStepHealth()
    {
        StartMatch();
        DamageOutcome outcome = default;
        _control.DamagePlayer(7, 30, value => outcome = value);

        _server.Tick();

        Assert.Null(outcome.Error);
        Assert.False(outcome.Died);
        Assert.Equal(_server.Boot.Rules.Combat.MaxHealth - 30, outcome.RemainingHealth);
        Assert.Equal(_observer.LastUpdate!.Tick, outcome.AppliedTick);
    }

    [Fact]
    public void QueuedDamageDeathIsScoredSuicide()
    {
        StartMatch();
        DamageOutcome outcome = default;
        _control.DamagePlayer(7, 500, value => outcome = value);

        _server.Tick();

        Assert.True(outcome.Died);
        Assert.Equal(0, outcome.RemainingHealth);
        EliminationMsg elimination = _server.Link.Last<EliminationMsg>();
        Assert.Equal(7, elimination.VictimId);
        Assert.Equal(7, elimination.KillerId);
        Assert.True(elimination.Flags.HasFlag(EliminationFlags.SUICIDE));
    }

    [Fact]
    public void QueuedDamageDeathReachesTheObserverUpdate()
    {
        StartMatch();
        _control.DamagePlayer(7, 500, _ => { });

        _server.Tick();

        ScoredKill scored = Assert.Single(_observer.LastUpdate!.Eliminations);
        Assert.Equal(DeathKind.SUICIDE, scored.Score.Kind);
        Assert.Equal(7, scored.Score.VictimId);
    }

    [Fact]
    public void AdministrativeCompletionRunsBeforeReplication()
    {
        E2EMatchControl control = new();
        using TestServer server = new(rules: new MatchConfig
        {
            Rules = new ModeRules { SpawnImmunity = 0 },
        }, control: control);
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Receive(7, new SetReadyMsg(true));
        server.Receive(8, new SetReadyMsg(true));
        server.Tick();
        server.Link.Messages.Clear();
        bool eliminationWasPublishedAtCompletion = true;
        control.DamagePlayer(7, 500, _ =>
            eliminationWasPublishedAtCompletion = server.Link.Messages.Any(
                sent => sent.Message is EliminationMsg));

        server.Tick();

        Assert.False(eliminationWasPublishedAtCompletion);
        Assert.Contains(server.Link.Messages, sent => sent.Message is EliminationMsg);
    }

    [Fact]
    public void DamageForAnUnknownPeerReportsAnErrorAndNeverThrows()
    {
        StartMatch();
        DamageOutcome outcome = default;
        _control.DamagePlayer(999, 10, value => outcome = value);

        _server.Tick();

        Assert.NotNull(outcome.Error);
        Assert.False(outcome.Died);
    }

    [Fact]
    public void StateIsServedFromTheFreshlySteppedWorld()
    {
        StartMatch();

        WorldStateOutcome state = ReadState();

        Assert.Equal(_observer.LastUpdate!.Tick, state.Tick);
        Assert.Equal([7, 8], state.Players.Select(player => player.PeerId).Order().ToArray());
    }

    [Fact]
    public void ObserverSeesJoinsLeavesAndThePhaseChange()
    {
        StartMatch();

        Assert.Equal(["join:7", "join:8", "phase:MATCH"], _observer.Trace);

        _server.Server.Disconnect(8);

        Assert.Contains("leave:8", _observer.Trace);
    }

    private WorldStateOutcome ReadState()
    {
        WorldStateOutcome state = default;
        _control.ReadState(value => state = value);
        _server.Tick();
        return state;
    }

    private void StartMatch()
    {
        _server.Connect(7, "alice");
        _server.Connect(8, "bob");
        _server.Receive(7, new SetReadyMsg(true));
        _server.Receive(8, new SetReadyMsg(true));
        _server.Tick();
    }
}
