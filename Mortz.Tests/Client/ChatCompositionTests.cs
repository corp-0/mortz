using Godot;
using Mortz.Client;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Server;
using Mortz.Shared;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class ChatCompositionTests
{
    [Fact]
    public void PopulatedLobbyPrioritizesPlayerSlotsAndChatOverSettings()
    {
        SceneTree tree = Assert.IsType<SceneTree>(Engine.GetMainLoop());
        PackedScene clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        tree.Root.AddChild(client);
        try
        {
            Lobby lobby = client.GetNode<Lobby>("Lobby");
            lobby.Visible = true;
            lobby.UpdatePlayers([1, 2], ["Host", "Guest"], [1, 0], 1);

            LobbySettingsPanel panel =
                client.GetNode<LobbySettingsPanel>("Lobby/Content/Main/Settings");
            MatchConfig config = new();
            panel.ApplySettingsForTest(new LobbySettingsMsg(
                "castlewars", "hash", ["castlewars"], ["Castle Wars"], config.ToBytes()));

            Assert.Equal(44, panel.RuleControlCount);
            Assert.Equal(MatchConfigUiMetadata.Categories.Count, panel.CategoryBlockCount);
            Assert.True(LobbySettingsPanel.CATEGORY_GAP >= 20,
                $"category gap was {LobbySettingsPanel.CATEGORY_GAP}");
            Assert.True(panel.CustomMinimumSize.X <= 460,
                $"settings minimum width was {panel.CustomMinimumSize.X}");
            Assert.True(panel.RulesMinimumHeight >= 250,
                $"rules minimum height was {panel.RulesMinimumHeight}");

            Control sidebar = client.GetNode<Control>("Lobby/Content/Main/Sidebar");
            Assert.True(sidebar.CustomMinimumSize.X >= 600,
                $"player/chat minimum width was {sidebar.CustomMinimumSize.X}");
            Control playerCard = client.GetNode<Control>(
                "Lobby/Content/Main/Sidebar/LobbyCard");
            Assert.True(playerCard.CustomMinimumSize.Y >= 350,
                $"player card minimum height was {playerCard.CustomMinimumSize.Y}");
            VBoxContainer players = client.GetNode<VBoxContainer>(
                "Lobby/Content/Main/Sidebar/LobbyCard/Margin/Column/PlayerScroll/Players");
            Assert.Equal(2, players.GetChildCount());
            Assert.All(players.GetChildren(), child =>
                Assert.True(((Control)child).CustomMinimumSize.Y >= 40));
            ChatPanel chat = client.GetNode<ChatPanel>(
                "Lobby/Content/Main/Sidebar/ChatPanel");
            Assert.True(chat.CustomMinimumSize.Y >= 290,
                $"chat minimum height was {chat.CustomMinimumSize.Y}");
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
        PackedScene clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        try
        {
            Assert.IsType<LobbySettingsPanel>(client.GetNode("Lobby/Content/Main/Settings"));
        }
        finally
        {
            client.Free();
        }

        AssertSceneType<BoolRuleControl>("BoolRuleControl");
        AssertSceneType<IntRuleControl>("IntRuleControl");
        AssertSceneType<FloatRuleControl>("FloatRuleControl");
        AssertSceneType<EnumRuleControl>("EnumRuleControl");

        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        MapPackage map = Assert.IsType<MapPackage>(MapPackage.Load("castlewars", contentRoot));
        Image preview = LobbySettingsPanel.ComposePreview(map);
        Assert.Equal(map.Width, preview.GetWidth());
        Assert.Equal(map.Height, preview.GetHeight());
    }

    [Fact]
    public void ClientSceneProvidesChatAboveLobbyAndDynamicGameViews()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain root = scene.Instantiate<ClientMain>();
        try
        {
            Assert.IsType<ClientChat>(root.GetNode("ClientChat"));
            Assert.IsType<ClientSessionController>(root.GetNode("Session"));
            Assert.IsType<ChatPanel>(root.GetNode("Lobby/Content/Main/Sidebar/ChatPanel"));
            Assert.IsType<LobbySettingsPanel>(root.GetNode("Lobby/Content/Main/Settings"));
        }
        finally
        {
            root.Free();
        }

        PackedScene gameScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/GameView.tscn");
        GameView game = gameScene.Instantiate<GameView>();
        try
        {
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
            "res://src/Shared/Scenes/ServerMain.tscn");
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

    private static void AssertSceneType<T>(string name) where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            $"res://src/Shared/Scenes/{name}.tscn");
        T node = scene.Instantiate<T>();
        node.Free();
    }
}
