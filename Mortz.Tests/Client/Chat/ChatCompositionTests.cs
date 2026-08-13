using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Announcements;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Players;
using Mortz.Client.Session;
using Mortz.Client.Setup;
using Mortz.Client.Spectating;
using Mortz.Client.Stats;
using Mortz.Client.Ui;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net.Lobby;
using Mortz.Extensions;
using Mortz.Net;
using Mortz.Server;
using Mortz.Server.Hosting;
using Mortz.Server.Pump;
using Mortz.Server.Query;
using Mortz.Shared;
using Mortz.Tests.Net;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;
using Physics = Mortz.Core.Match.Configuration.Physics;

namespace Mortz.Tests.Client.Chat;

[Collection(nameof(MortzGodotCollection))]
public class ChatCompositionTests : NodeServiceTest
{
    [Fact]
    public void PopulatedLobbyComposesPlayerSlotsChatAndSettings()
    {
        Lobby lobby = MountLobby();

        new LobbyStateMsg(
        [
            new LobbyMember(1, "Host", true, null),
            new LobbyMember(2, "Guest", false, null),
        ], []).Broadcast();
        new LobbySettingsMsg("castlewars", "hash", ["castlewars"], ["Castle Wars"],
            [], [], "", new MatchConfig().ToBytes()).Broadcast();

        UiPropertySheet rulesSheet = lobby.GetDescendantByType<UiPropertySheet>(
            sheet => sheet.BoundModelType == typeof(ModeRules));
        UiPropertySheet physicsSheet = lobby.GetDescendantByType<UiPropertySheet>(
            sheet => sheet.BoundModelType == typeof(Physics));
        VictoryRulesSheet victorySheet = lobby.GetDescendantByType<VictoryRulesSheet>();
        Assert.Equal(
            ModeRulesUiMetadata.Categories.Sum(category => category.Properties.Count),
            rulesSheet.ControlCount);
        Assert.Equal(ModeRulesUiMetadata.Categories.Count, rulesSheet.CategoryBlockCount);
        Assert.Equal(
            PhysicsUiMetadata.Categories.Sum(category => category.Properties.Count),
            physicsSheet.ControlCount);
        Assert.Equal(PhysicsUiMetadata.Categories.Count, physicsSheet.CategoryBlockCount);
        Assert.IsType<KillsVictoryRules>(victorySheet.Rules);
        Assert.True(rulesSheet.Visible);
        BoolPropertyControl friendlyFire = rulesSheet.GetDescendantByType<BoolPropertyControl>(
            control => control.GetDescendantByType<Label>().Text == "Friendly Fire");
        Assert.False(friendlyFire.Visible);

        rulesSheet.UpdateModel(new ModeRules { Teams = true });

        Assert.True(friendlyFire.Visible);

        SingleColumnRoster roster = lobby.GetDescendantByType<SingleColumnRoster>();
        VBoxContainer players = roster.GetDescendantByType<VBoxContainer>();
        Assert.Equal(2, players.GetChildCount());
        Assert.IsType<LobbyChat>(lobby.GetDescendantByType<LobbyChat>());
    }

    [Fact]
    public void LobbySettingsAndTypedControlsAreComposed()
    {
        Lobby lobby = InstantiateLobby();
        try
        {
            Assert.IsType<LobbySettingsPanel>(
                lobby.GetDescendantByType<LobbySettingsPanel>());
        }
        finally
        {
            lobby.Free();
        }

        AssertSceneType<BoolPropertyControl>("BoolPropertyControl");
        AssertSceneType<IntPropertyControl>("IntPropertyControl");
        AssertSceneType<FloatPropertyControl>("FloatPropertyControl");
        AssertSceneType<EnumPropertyControl>("EnumPropertyControl");
        AssertSceneType<UiPropertySheet>("UiPropertySheet");
        AssertSceneType<VictoryRulesSheet>("VictoryRulesSheet");

        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        GameContent content = Assert.IsType<GameContent>(GameContent.Load(contentRoot));
        MapPackage map = Assert.IsType<MapPackage>(content.LoadMap("castlewars"));
        Image preview = LobbySettingsPanel.ComposePreview(map);
        Assert.Equal(map.Width, preview.GetWidth());
        Assert.Equal(map.Height, preview.GetHeight());
    }

    [Fact]
    public void MapPreviewComposesWhateverFormatsTheLayersDecodeTo()
    {
        // BlendRect skips layers when their formats differ.
        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        GameContent content = Assert.IsType<GameContent>(GameContent.Load(contentRoot));
        MapPackage map = Assert.IsType<MapPackage>(content.LoadMap("fightbox"));
        Assert.NotEqual(Image.Format.Rgba8, map.Background.GetFormat());

        Image preview = LobbySettingsPanel.ComposePreview(map);

        Assert.Equal(Image.Format.Rgba8, preview.GetFormat());
        // ComposePreview must not mutate the shared image.
        Assert.Equal(Image.Format.Rgba8, map.Solid.GetFormat());
        Assert.Equal(map.Width, preview.GetWidth());
        Assert.Equal(map.Height, preview.GetHeight());
    }

    [Fact]
    public void EachScreenComposesItsOwnChat()
    {
        Lobby lobby = InstantiateLobby();
        try
        {
            Assert.IsType<ClientChat>(lobby.GetDescendantByType<ClientChat>());
            Assert.IsType<LobbyChat>(lobby.GetDescendantByType<LobbyChat>());
        }
        finally
        {
            lobby.Free();
        }

        PackedScene gameScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn");
        GameView game = gameScene.Instantiate<GameView>();
        try
        {
            Assert.IsType<AnnouncementChat>(game.GetDescendantByType<AnnouncementChat>());
            Assert.IsType<ClientChat>(game.GetDescendantByType<ClientChat>());
            Assert.IsType<GameChat>(game.GetDescendantByType<GameChat>());
            Assert.IsType<SpectatorController>(
                game.GetDescendantByType<SpectatorController>());
            Assert.IsType<SpectatorHud>(game.GetDescendantByType<SpectatorHud>());
            Assert.Equal(2, game.GetChildren().OfType<Camera2D>().Count());
        }
        finally
        {
            game.Free();
        }
    }

    [Fact]
    public void ServerMainHostsTheHostPumpAndQuery()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Root/ServerMain.tscn");
        ServerMain root = scene.Instantiate<ServerMain>();
        try
        {
            Assert.IsType<ServerHost>(root.GetDescendantByType<ServerHost>());
            Assert.IsType<ServerPump>(root.GetDescendantByType<ServerPump>());
            Assert.IsType<ServerQueryResponder>(
                root.GetDescendantByType<ServerQueryResponder>());
        }
        finally
        {
            root.Free();
        }
    }

    private static Lobby InstantiateLobby() =>
        ResourceLoader.Load<PackedScene>("res://src/Shared/UI/Menus/Lobby.tscn")
            .Instantiate<Lobby>();

    private Lobby MountLobby()
    {
        Lobby lobby = InstantiateLobby();
        HostServiceRoot().AddChild(lobby);
        return lobby;
    }

    private ServiceRoot HostServiceRoot()
    {
        FakeNetwork network = new() { LocalPeerId = 1 };
        ClientAdmin admin = new();
        admin.FakeDependency<INetwork>(network);
        admin.FakeDependency(Router);
        ClientPlayers players = HostRouted(new ClientPlayers());
        Pings pings = new();
        pings.FakeDependency(players);
        SessionWins wins = new();
        wins.FakeDependency(players);
        return Host(new ServiceRoot
        {
            Setup = HostRouted(new MatchSetup()),
            Pings = HostRouted(pings),
            Wins = HostRouted(wins),
            Players = players,
            Admin = Host(admin),
            Network = network,
            Router = Router,
            SessionExit = new FakeSessionExit(),
        });
    }

    private static void AssertSceneType<T>(string name) where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            $"res://src/Shared/UI/Controls/{name}.tscn");
        T node = scene.Instantiate<T>();
        node.Free();
    }
}
