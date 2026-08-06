using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Spectating;
using Mortz.Core.Match;
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
        players.OpenMatch();
        _controller.FakeDependency(players);
        _controller.Initialize(MatchParticipation.JipSpectator, Vector2.Zero);
        HostRouted(_controller);
    }

    [Fact]
    public void MatchEndHidesTheSpectatorStatusForTheRestOfTheMatch()
    {
        _controller.Present([], null, newestTick: 0);
        Assert.True(_hud.Visible);

        MatchProtocol.Encode(new Victor.Player(2)).Broadcast();
        Assert.False(_hud.Visible);

        _controller.Present([], null, newestTick: 0);
        Assert.False(_hud.Visible);
    }
}
