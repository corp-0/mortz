using Godot;
using Mortz.Core.Net;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Server.Hosting;

/// <summary>Owns the listening transport. Loads the boot record here so the
/// port is settled before any sibling binds a socket.</summary>
public partial class ServerHost : Node
{
    private static readonly ILogger _log = MortzLog.For("server");

    [Export] private string _defaultMap = "castlewars";

    public ServerBootLoad? Load { get; private set; }

    public override void _Ready() => Load = ServerBootLoader.TryLoad(_defaultMap);

    public bool Listen(NetworkManager network)
    {
        if (Load is not ServerBootLoad load)
            return false;
        ServerBoot boot = load.Boot;
        Error error = network.StartServer(boot.GamePort);
        if (error != Error.Ok)
        {
            _log.Error("failed to listen on port {Port}: {Error}", boot.GamePort, error);
            return false;
        }

        _log.Information(
            "'{Name}' listening on port {Port} (protocol v{Protocol}, " +
            "map '{Map}' {Width}x{Height}, tick {TickRate} Hz)",
            boot.Name, network.BoundPort(), NetConfig.PROTOCOL_VERSION, boot.Map.DisplayName,
            boot.Map.Width, boot.Map.Height, SimConfig.TICK_RATE);
        return true;
    }
}
