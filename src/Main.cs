using Godot;
using Mortz.Shared;

namespace Mortz;

/// <summary>
/// Boot gate. Decides once, at startup, whether this process is a dedicated
/// server or a game client; the two never mix. Exported server builds carry
/// the "dedicated_server" feature tag; dev runs use the --server user arg.
/// </summary>
public partial class Main : Node
{
    [Export] private PackedScene _clientScene = null!;
    [Export] private PackedScene _serverScene = null!;

    public override void _Ready()
    {
        bool serverMode = OS.HasFeature("dedicated_server") || CmdArgs.HasFlag("--server");
        if (!serverMode && CmdArgs.HasFlag("--windowed"))
            ForceWindowed();
        AddChild((serverMode ? _serverScene : _clientScene).Instantiate());
    }

    private static void ForceWindowed()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(
            (int)ProjectSettings.GetSetting("display/window/size/viewport_width"),
            (int)ProjectSettings.GetSetting("display/window/size/viewport_height")));
    }
}
