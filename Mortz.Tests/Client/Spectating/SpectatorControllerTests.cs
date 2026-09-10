using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Replication;
using Mortz.Client.Spectating;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Runtime.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client.Spectating;

[Collection(nameof(MortzGodotCollection))]
public class SpectatorControllerTests : NodeServiceTest
{
    private readonly SpectatorController _controller;
    private readonly SpectatorHud _hud;
    private readonly Camera2D _camera;
    private readonly ClientMatchState _matchState;

    public SpectatorControllerTests()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        _controller = shell.GetNode<SpectatorController>("SpectatorController");
        _hud = shell.GetNode<SpectatorHud>("Hud/SpectatorHud");
        _camera = Assert.IsType<Camera2D>(_controller.Get("_camera").AsGodotObject());
        shell.RemoveChild(_controller);
        _hud.GetParent().RemoveChild(_hud);
        shell.RemoveChild(_camera);
        shell.Free();
        Host(_hud);
        Host(_camera);

        _controller.FakeDependency<INetwork>(new FakeNetwork { LocalPeerId = 1 });
        ClientPlayers players = RegisterRuntime(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        _controller.FakeDependency(players);
        _matchState = new ClientMatchState(3, MatchParticipation.JipSpectator);
        _controller.FakeDependency(_matchState);
        _controller.Initialize(Vector2.Zero);
        Host(_controller);
        Router.MatchGeneration = _matchState.Generation;
        ClientMatchStateAdapter adapter = new(_matchState);
        RegisterRuntime(adapter);
    }

    [Fact]
    public void UnscheduledDeathAndExpiredScheduleKeepTheDeathPresentationVisible()
    {
        Label status = Assert.IsType<Label>(_hud.Get("_status").AsGodotObject());
        new MatchParticipationMsg(MatchSeat.PLAYER, MatchActivity.DEATH_PRESENTATION,
            SpectateReason.RESPAWN, -1).Broadcast(Router);
        _controller.Present([], new Vector2(200, 200), newestTick: 60);
        Assert.Equal("Waiting to respawn", status.Text);
        Assert.True(_hud.Visible);

        new MatchParticipationMsg(MatchSeat.PLAYER, MatchActivity.DEATH_PRESENTATION,
            SpectateReason.RESPAWN, 120).Broadcast(Router);
        _controller.Present([], new Vector2(200, 200), newestTick: 60);
        Assert.Equal("Respawning in 1.0", status.Text);
        _controller.Present([], new Vector2(200, 200), newestTick: 130);
        Assert.Equal("Respawning...", status.Text);
        Assert.True(_hud.Visible);
        Assert.Equal(MatchActivity.DEATH_PRESENTATION, _matchState.Participation.Activity);
    }

    [Fact]
    public void SpectatorCameraSkipsAnUnscheduledDeadPlayer()
    {
        RenderPlayer dead = new(2, new Vec2(100, 100), 0, 0, RopeMode.NONE, default,
            0, 0, 0, 0, 0, 0, 0, default);
        RenderPlayer alive = dead with { PeerId = 3, Position = new Vec2(300, 200), Health = 100 };
        _controller.Present([dead, alive], null, newestTick: 10);
        Assert.Equal(new Vector2(300, 200 - SimConfig.PLAYER_HALF_HEIGHT), _camera.GlobalPosition);

        _controller.Present([dead, alive with { Health = 0 }], null, newestTick: 11);
        Assert.Equal(Vector2.Zero, _camera.GlobalPosition);
    }

    [Fact]
    public void MatchEndHidesTheSpectatorStatusForTheRestOfTheMatch()
    {
        _controller.Present([], null, newestTick: 0);
        Assert.True(_hud.Visible);

        MatchProtocol.Encode(new Victor.Player(2)).Broadcast(Router);
        Assert.False(_hud.Visible);

        _controller.Present([], null, newestTick: 0);
        Assert.False(_hud.Visible);
    }

    [Fact]
    public void InitialAndLiveParticipationProduceTheSamePresentation()
    {
        MatchParticipation initial = MatchParticipation.JipSpectator;
        _controller.Present([], null, newestTick: 0);
        bool initialVisible = _hud.Visible;

        new MatchParticipationMsg(
            MatchSeat.PLAYER, MatchActivity.ACTIVE, SpectateReason.NONE, -1).Broadcast(Router);
        Assert.False(_hud.Visible);

        MatchParticipation? emitted = null;
        _matchState.ParticipationChanged += participation => emitted = participation;
        new MatchParticipationMsg(
            initial.Seat, initial.Activity, initial.Reason, initial.ReturnTick).Broadcast(Router);
        _controller.Present([], null, newestTick: 0);

        Assert.Equal(initial, emitted);
        Assert.Equal(initialVisible, _hud.Visible);
    }

    [Fact]
    public void InvalidParticipationUpdateIsIgnored()
    {
        int changes = 0;
        _matchState.ParticipationChanged += _ => changes++;

        new MatchParticipationMsg(
            MatchSeat.SPECTATOR, MatchActivity.ACTIVE, SpectateReason.NONE, -1).Broadcast(Router);
        _controller.Present([], null, newestTick: 0);

        Assert.Equal(0, changes);
        Assert.True(_hud.Visible);
    }
}
