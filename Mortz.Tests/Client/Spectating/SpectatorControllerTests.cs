using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Spectating;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Net.Match;
using Mortz.Net;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client.Spectating;

[Collection(nameof(MortzGodotCollection))]
public class SpectatorControllerTests : NodeServiceTest
{
    private readonly SpectatorController _controller;
    private readonly SpectatorHud _hud;
    private readonly ClientMatchState _matchState;

    public SpectatorControllerTests()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        _controller = shell.GetNode<SpectatorController>("SpectatorController");
        _hud = shell.GetNode<SpectatorHud>("Hud/SpectatorHud");
        Camera2D camera = shell.GetNode<Camera2D>("MatchCamera");
        shell.RemoveChild(_controller);
        _hud.GetParent().RemoveChild(_hud);
        shell.RemoveChild(camera);
        shell.Free();
        Host(_hud);
        Host(camera);

        _controller.FakeDependency<INetwork>(new FakeNetwork { LocalPeerId = 1 });
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        _controller.FakeDependency(players);
        _matchState = new ClientMatchState(3, MatchParticipation.JipSpectator);
        _controller.FakeDependency(_matchState);
        _controller.Initialize(Vector2.Zero);
        Host(_controller);
        ClientMatchStateAdapter adapter = new();
        adapter.Initialize(_matchState);
        HostRouted(adapter);
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
