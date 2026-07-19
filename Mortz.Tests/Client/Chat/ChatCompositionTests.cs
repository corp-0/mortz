using Godot;
using Mortz.Client;
using Mortz.Client.Chat;
using Mortz.Client.Feed;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Setup;
using Mortz.Client.Ui;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Server;
using Mortz.Server.Chat;
using Mortz.Shared;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client.Chat;

[Collection(nameof(GodotHeadlessCollection))]
public class ChatCompositionTests
{
    [Fact]
    public void PopulatedLobbyComposesPlayerSlotsChatAndSettings()
    {
        SceneTree tree = Assert.IsType<SceneTree>(Engine.GetMainLoop());
        PackedScene clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Root/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        tree.Root.AddChild(client);
        try
        {
            Lobby lobby = InstantiateLobby();
            client.AddChild(lobby);
            MatchSetup setup = client.GetNode<MatchSetup>("MatchSetup");
            setup.ApplyLobbyStateForTest(new LobbyStateMsg(
                [1, 2], ["Host", "Guest"], [1, 0], [0, 0], [], []));

            MatchConfig config = new();
            setup.ApplySettingsForTest(new LobbySettingsMsg(
                "castlewars", "hash", ["castlewars"], ["Castle Wars"], config.ToBytes()));
            UiPropertySheet sheet = lobby.GetNode<UiPropertySheet>(
                "Content/Main/Settings/Margin/Column/RulesScroll/Rules");
            Assert.Equal(
                MatchConfigUiMetadata.Categories.Sum(category => category.Properties.Count),
                sheet.ControlCount);
            Assert.Equal(MatchConfigUiMetadata.Categories.Count, sheet.CategoryBlockCount);

            VBoxContainer players = lobby.GetNode<VBoxContainer>(
                "Content/Main/Sidebar/LobbyCard/Margin/Column/Roster/" +
                "SingleColumnRoster/Players");
            Assert.Equal(2, players.GetChildCount());
            Assert.IsType<ChatPanel>(lobby.GetNode("Content/Main/Sidebar/ChatPanel"));
        }
        finally
        {
            tree.Root.RemoveChild(client);
            client.Free();
        }
    }

    [Fact]
    public void LobbySettingsAndTypedControlsAreComposed()
    {
        Lobby lobby = InstantiateLobby();
        try
        {
            Assert.IsType<LobbySettingsPanel>(lobby.GetNode("Content/Main/Settings"));
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

        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        MapPackage map = Assert.IsType<MapPackage>(MapPackage.Load("castlewars", contentRoot));
        Image preview = LobbySettingsPanel.ComposePreview(map);
        Assert.Equal(map.Width, preview.GetWidth());
        Assert.Equal(map.Height, preview.GetHeight());
    }

    [Fact]
    public void MapPreviewComposesWhateverFormatsTheLayersDecodeTo()
    {
        // fightbox ships an RGB background (no alpha); BlendRect would
        // silently skip mismatched layers, leaving a background-only preview.
        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        MapPackage map = Assert.IsType<MapPackage>(MapPackage.Load("fightbox", contentRoot));
        Assert.NotEqual(Image.Format.Rgba8, map.Background.GetFormat());

        Image preview = LobbySettingsPanel.ComposePreview(map);

        Assert.Equal(Image.Format.Rgba8, preview.GetFormat());
        Assert.Equal(Image.Format.Rgba8, map.Solid.GetFormat()); // untouched shared image
        Assert.Equal(map.Width, preview.GetWidth());
        Assert.Equal(map.Height, preview.GetHeight());
    }

    [Fact]
    public void EachScreenComposesItsOwnChat()
    {
        Lobby lobby = InstantiateLobby();
        try
        {
            Assert.IsType<ClientChat>(lobby.GetNode("ClientChat"));
            Assert.IsType<ChatPanel>(lobby.GetNode("Content/Main/Sidebar/ChatPanel"));
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
            Assert.IsType<KillFeed>(game.GetNode("KillFeed"));
            Assert.IsType<ClientChat>(game.GetNode("ClientChat"));
            Assert.IsType<ChatPanel>(game.GetNode("Hud/ChatPanel"));
        }
        finally
        {
            game.Free();
        }
    }

    [Fact]
    public void ServerChatIsAnInjectedChildFeature()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Root/ServerMain.tscn");
        ServerMain root = scene.Instantiate<ServerMain>();
        try
        {
            Assert.IsType<ServerHost>(root.GetNode("Host"));
            Assert.IsType<ServerSessionController>(root.GetNode("Session"));
            Assert.IsType<ServerChat>(root.GetNode("Chat"));
            Assert.IsType<ServerLobbySettings>(root.GetNode("LobbySettings"));
        }
        finally
        {
            root.Free();
        }
    }

    private static Lobby InstantiateLobby() =>
        ResourceLoader.Load<PackedScene>("res://src/Shared/UI/Menus/Lobby.tscn")
            .Instantiate<Lobby>();

    private static void AssertSceneType<T>(string name) where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            $"res://src/Shared/UI/Controls/{name}.tscn");
        T node = scene.Instantiate<T>();
        node.Free();
    }
}
