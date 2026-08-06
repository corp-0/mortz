using Godot;
using Mortz.Core.Net.Query;
using Mortz.Server.Hosting;
using Mortz.Server.Pump;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;
#if TOOLS
using Mortz.Shared.E2E;
#endif

namespace Mortz.Server.Query;

/// <summary>
/// Answers server-browser queries on a small UDP socket beside the game port.
/// ENet owns the game port and only talks to peers that finish Hello, so a
/// browser that must not join has nowhere else to ask.
/// </summary>
public partial class ServerQueryResponder : Node
{
    private static readonly ILogger _log = MortzLog.For("server");

    private const int MAX_PACKETS_PER_FRAME = 32;

    private readonly ServerQueryRateLimiter _limiter = new();

    [Export] private ServerHost _host = null!;
    [Export] private ServerPump _pump = null!;

    private PacketPeerUdp? _socket;

    /// <summary>The port the responder bound, -1 when it never did.</summary>
    public int BoundQueryPort { get; private set; } = -1;

    public override void _Ready()
    {
        if (_host.Load is not ServerBootLoad load)
            return;
#if TOOLS
        // Under E2E the OS picks the game port, so the port derived from it is
        // meaningless; a scenario that wants the responder names one.
        if (E2ELaunch.Enabled && CmdArgs.GetValue("--query-port") == null)
            return;
#endif
        int queryPort = load.Boot.QueryPort;
        PacketPeerUdp socket = new PacketPeerUdp();
#if TOOLS
        Error error = E2ELaunch.Enabled
            ? socket.Bind(queryPort, "127.0.0.1")
            : socket.Bind(queryPort);
#else
        Error error = socket.Bind(queryPort);
#endif
        if (error != Error.Ok)
        {
            // Still playable, just invisible to server browsers.
            _log.Error("query port {Port} unavailable: {Error}", queryPort, error);
            socket.Dispose();
            return;
        }
        _socket = socket;
        BoundQueryPort = queryPort;
        _log.Information("answering browser queries on port {Port}", queryPort);
    }

    public override void _ExitTree()
    {
        _socket?.Close();
        _socket = null;
        BoundQueryPort = -1;
    }

    public override void _Process(double delta)
    {
        if (_socket is not PacketPeerUdp socket)
            return;
        ulong now = Time.GetTicksMsec();
        for (int i = 0; i < MAX_PACKETS_PER_FRAME && socket.GetAvailablePacketCount() > 0; i++)
        {
            byte[] datagram = socket.GetPacket();
            string source = socket.GetPacketIP();
            int port = socket.GetPacketPort();
            if (!ServerQueryProtocol.TryDecodeRequest(datagram, out uint nonce))
                continue;
            if (!_limiter.Allow(source, now))
                continue;
            socket.SetDestAddress(source, port);
            socket.PutPacket(
                ServerQueryProtocol.EncodeResponse(nonce, _pump.Server.Describe()));
        }
    }
}
