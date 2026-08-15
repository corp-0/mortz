using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Spectating;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Net;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class MatchEndPresentationTests : NodeServiceTest
{
    [Fact]
    public void OneMatchEndMessageReachesBothPresentationsWithTheSameDecodedVictor()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        WinnerBanner banner = shell.GetNode<WinnerBanner>("Hud/WinnerBanner");
        Label winnerLabel = banner.GetNode<Label>("Winner");
        SpectatorController spectator = shell.GetNode<SpectatorController>("SpectatorController");
        SpectatorHud spectatorHud = shell.GetNode<SpectatorHud>("Hud/SpectatorHud");
        Camera2D camera = shell.GetNode<Camera2D>("MatchCamera");

        banner.GetParent().RemoveChild(banner);
        shell.RemoveChild(spectator);
        spectatorHud.GetParent().RemoveChild(spectatorHud);
        shell.RemoveChild(camera);
        shell.Free();

        Host(spectatorHud);
        Host(camera);
        ClientPlayers players = HostRouted(new ClientPlayers());
        new LobbyStateMsg([new LobbyMember(7, "alice", false, null)], []).Broadcast(Router);
        ClientMatchState matchState = new(3, MatchParticipation.JipSpectator);

        banner.FakeDependency(players);
        banner.FakeDependency(matchState);
        Host(banner);
        spectator.FakeDependency<INetwork>(new FakeNetwork { LocalPeerId = 1 });
        spectator.FakeDependency(players);
        spectator.FakeDependency(matchState);
        spectator.Initialize(Vector2.Zero);
        Host(spectator);
        ClientMatchStateAdapter adapter = new();
        adapter.Initialize(matchState);
        HostRouted(adapter);
        spectator.Present([], null, newestTick: 0);
        Assert.True(spectatorHud.Visible);

        Victor expected = new Victor.Player(7);
        MatchEndMsg message = MatchProtocol.Encode(expected);
        Assert.True(MatchProtocol.TryDecode(message, out Victor? decoded));
        int changes = 0;
        matchState.WinnerChanged += _ => changes++;
        message.Broadcast(Router);
        message.Broadcast(Router);

        Assert.Equal(expected, decoded);
        Assert.Equal(expected, matchState.Winner);
        Assert.Equal(1, changes);
        Assert.Equal("alice wins!", winnerLabel.Text);
        Assert.True(winnerLabel.Visible);
        spectator.Present([], null, newestTick: 0);
        Assert.False(spectatorHud.Visible);
    }
}
