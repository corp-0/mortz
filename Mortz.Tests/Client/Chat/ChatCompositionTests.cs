using Godot;
using Mortz.Client;
using Mortz.Client.Chat;
using Mortz.Client.Menus;
using Mortz.Client.Session;
using Mortz.Client.Setup;
using Mortz.Core.Match;
using Mortz.Core.Net;
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
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        tree.Root.AddChild(client);
        try
        {
            Lobby lobby = client.GetNode<Lobby>("Lobby");
            lobby.Visible = true;
            MatchSetup setup = client.GetNode<MatchSetup>("MatchSetup");
            setup.ApplyLobbyStateForTest(new LobbyStateMsg(
                [1, 2], ["Host", "Guest"], [1, 0], [0, 0], [], []));

            LobbySettingsPanel panel =
                client.GetNode<LobbySettingsPanel>("Lobby/Content/Main/Settings");
            MatchConfig config = new();
            setup.ApplySettingsForTest(new LobbySettingsMsg(
                "castlewars", "hash", ["castlewars"], ["Castle Wars"], config.ToBytes()));
            Assert.Equal(
                MatchConfigUiMetadata.Categories.Sum(category => category.Properties.Count),
                panel.RuleControlCount);
            Assert.Equal(MatchConfigUiMetadata.Categories.Count, panel.CategoryBlockCount);

            VBoxContainer players = client.GetNode<VBoxContainer>(
                "Lobby/Content/Main/Sidebar/LobbyCard/Margin/Column/Roster/" +
                "SingleColumnRoster/Players");
            Assert.Equal(2, players.GetChildCount());
            Assert.IsType<ChatPanel>(client.GetNode(
                "Lobby/Content/Main/Sidebar/ChatPanel"));
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
    public void KillFeedLinesLandInChatWithResolvedNames()
    {
        SceneTree tree = Assert.IsType<SceneTree>(Engine.GetMainLoop());
        PackedScene clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        tree.Root.AddChild(client);
        NetTransport.SendDelegate original = NetTransport.Send;
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, 1, payload, isServer: false));
        try
        {
            new RosterMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [0, 1]).Broadcast();
            new EliminationMsg(1, 2, EliminationFlags.NONE, 1, 1, 0, 0).Broadcast();

            ClientChat chat = client.GetNode<ClientChat>("ClientChat");
            Assert.Contains(chat.State.Entries, entry => entry.Text == "Alice killed Bob");
        }
        finally
        {
            NetTransport.Send = original;
            tree.Root.RemoveChild(client);
            client.Free();
        }
    }

    [Fact]
    public void ClientSceneProvidesChatAboveTheLobby()
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
