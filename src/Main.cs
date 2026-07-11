using Godot;
using Mortz.Client;
using Mortz.Server;
using Mortz.Shared;

namespace Mortz;

/// <summary>
/// Boot gate. Decides once, at startup, whether this process is a dedicated
/// server or a game client; the two never mix. Exported server builds carry
/// the "dedicated_server" feature tag; dev runs use the --server user arg.
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        bool serverMode = OS.HasFeature("dedicated_server") || CmdArgs.HasFlag("--server");
        if (serverMode)
            AddChild(new ServerMain { Name = "ServerMain" });
        else
            AddChild(new ClientMain { Name = "ClientMain" });
    }
}
