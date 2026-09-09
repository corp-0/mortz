using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;
using Mortz.Shared;
using Mortz.Shared.Logging;

namespace Mortz;

/// <summary>Boot gate: decides once, at startup, whether this process is a
/// dedicated server or a game client.</summary>
[Meta(typeof(IAutoNode))]
public partial class Main : Node, IProvide<NetworkManager>
{
    [Export] private NetworkManager _network = null!;
    [Export] private PackedScene _clientScene = null!;
    [Export] private PackedScene _serverScene = null!;

    NetworkManager IProvide<NetworkManager>.Value() => _network;
    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        this.Provide();
        bool serverMode = RunMode.IsDedicatedServer;
        if (!serverMode && !CmdArgs.HasFlag("--windowed") && !OS.HasFeature("editor"))
            GoFullScreen();
        AddChild((serverMode ? _serverScene : _clientScene).Instantiate());
    }

    public override void _ExitTree() => MortzLog.Flush();

    private static void GoFullScreen()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
    }
}
