using Godot;
using Mortz.Core.Net;
using Mortz.Core.Sim;
using Mortz.Net;

namespace Mortz.Server.Hosting;

/// <summary>Owns the listening transport. Loads ServerBootConfig here so the
/// port is settled before any sibling binds a socket.</summary>
public partial class ServerHost : Node
{
    [Export] private string _defaultMap = "castlewars";

    public ServerBootConfig? Config { get; private set; }

    public override void _Ready() => Config = ServerBootConfig.TryLoad(_defaultMap);

    public bool Listen(NetworkManager network)
    {
        if (Config is not { } config)
            return false;
        Error error = network.StartServer(config.GamePort);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[server] failed to listen on port {config.GamePort}: {error}");
            return false;
        }

        GD.Print($"[server] '{config.ServerName}' listening on port {config.GamePort} " +
                 $"(protocol v{NetConfig.PROTOCOL_VERSION}, " +
                 $"map '{config.Map.DisplayName}' {config.Map.Width}x{config.Map.Height}, " +
                 $"tick {SimConfig.TICK_RATE} Hz)");
        return true;
    }
}
