using System.Collections.Immutable;
using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net.Chat;
using Mortz.Net;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class PlayerViewTypingTests : NodeServiceTest
{
    private readonly PlayerViewManager _manager;

    public PlayerViewTypingTests()
    {
        _manager = TakeManagerFromGameViewScene();
        _manager.FakeDependency<INetwork>(new FakeNetwork { LocalPeerId = 1 });
        _manager.FakeDependency<ISfx>(new NullSfx());
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        _manager.FakeDependency(players);
        HostRouted(_manager);
    }

    [Fact]
    public void RemoteBalloonsFollowSessionTypingOnBothSidesOfTheSpawn()
    {
        new TypingStateMsg(2, true).Broadcast(Router);
        _manager.BeginFrame();
        _manager.Place(2, ViewState());
        _manager.Place(3, ViewState());

        Assert.True(Balloon(2).Visible);
        Assert.False(Balloon(3).Visible);

        new TypingStateMsg(3, true).Broadcast(Router);
        _manager.Place(3, ViewState());
        Assert.True(Balloon(3).Visible);

        new TypingStateMsg(3, false).Broadcast(Router);
        _manager.Place(3, ViewState());
        Assert.False(Balloon(3).Visible);
    }

    [Fact]
    public void LocalBalloonFollowsTheChatGuardNotTheBroadcast()
    {
        object owner = new();
        try
        {
            _manager.BeginFrame();
            _manager.Place(1, ViewState());
            Assert.False(Balloon(1).Visible);

            ChatInputGuard.SetTyping(owner, true);
            _manager.Place(1, ViewState());
            Assert.True(Balloon(1).Visible);

            ChatInputGuard.SetTyping(owner, false);
            new TypingStateMsg(1, true).Broadcast(Router);
            _manager.Place(1, ViewState());
            Assert.False(Balloon(1).Visible);
        }
        finally
        {
            ChatInputGuard.SetTyping(owner, false);
        }
    }

    [Fact]
    public void RendererReplayModeUsesRecordedStateAndHidesLiveTyping()
    {
        new TypingStateMsg(2, true).Broadcast(Router);
        MortarViewManager mortars = new();
        RopeOverlay ropes = new();
        MatchSceneRenderer renderer = new(_manager, mortars, ropes);
        PresentedMatchFrame frame = new(
            10,
            [new PresentedPlayer(2, ViewState() with { Skin = 4 })],
            ImmutableArray<PresentedMortar>.Empty,
            ImmutableArray<RopeSegment>.Empty);

        renderer.Apply(frame, MatchRenderMode.REPLAY);

        Sprite2D body = _manager.ViewForTest(2).GetNode<Sprite2D>("PlayerSprite");
        Assert.Equal(4, body.Frame);
        Assert.False(Balloon(2).Visible);
        mortars.Free();
        ropes.Free();
    }

    private AnimatedSprite2D Balloon(int peerId) =>
        _manager.ViewForTest(peerId).GetNode<AnimatedSprite2D>("AnimatedSprite2D");

    private static PlayerViewManager TakeManagerFromGameViewScene()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        PlayerViewManager manager = shell.GetNode<PlayerViewManager>("Players");
        shell.RemoveChild(manager);
        shell.Free();
        return manager;
    }

    private static PlayerViewState ViewState() => new(
        Feet: new Vector2(200, 240), Aim: 0, Skin: 0, Ammo: 3, ReloadTicks: 0,
        Health: 3, RespawnTicks: 0, ParryTicks: 0, DashCooldown: 0, SpawnImmunityTicks: 0);
}
