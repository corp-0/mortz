using Godot;
using Mortz.Client;
using Mortz.Server;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class ChatCompositionTests
{
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
            Assert.IsType<ChatPanel>(root.GetNode("Lobby/ChatPanel"));
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
        }
        finally
        {
            root.Free();
        }
    }
}
