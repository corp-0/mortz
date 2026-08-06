using Godot;
using Mortz.Shared;
using Mortz.Shared.Logging;

namespace Mortz;

/// <summary>Boot gate: decides once, at startup, whether this process is a
/// dedicated server or a game client.</summary>
public partial class Main : Node
{
    [Export] private PackedScene _clientScene = null!;
    [Export] private PackedScene _serverScene = null!;

    public override void _Ready()
    {
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
