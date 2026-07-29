using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Mortz.Server.Hosting;
using Mortz.Server.Lobby;
using Mortz.Server.Session;

namespace Mortz.Server.Query;

/// <summary>
/// Answers server-browser queries on a small UDP socket beside the game port.
/// ENet owns the game port and only talks to peers that finish Hello, so a
/// browser that must not join has nowhere else to ask.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class ServerQueryResponder : Node
{
    private const int MAX_PACKETS_PER_FRAME = 32;

    private readonly ServerQueryRateLimiter _limiter = new();
    private PacketPeerUdp? _socket;

    [Dependency]
    private IServerIdentity Identity => this.DependOn<IServerIdentity>();

    [Dependency]
    private IServerSession Session => this.DependOn<IServerSession>();

    [Dependency]
    private IServerLobbySettings LobbySettings => this.DependOn<IServerLobbySettings>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        PacketPeerUdp socket = new PacketPeerUdp();
        Error error = socket.Bind(Identity.QueryPort);
        if (error != Error.Ok)
        {
            // Still playable, just invisible to server browsers.
            GD.PrintErr($"[server] query port {Identity.QueryPort} unavailable: {error}");
            socket.Dispose();
            return;
        }
        _socket = socket;
        GD.Print($"[server] answering browser queries on port {Identity.QueryPort}");
    }

    public void OnExitTree()
    {
        _socket?.Close();
        _socket = null;
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
            socket.PutPacket(ServerQueryProtocol.EncodeResponse(nonce, Describe()));
        }
    }

    private ServerInfo Describe()
    {
        return new ServerInfo(
            Identity.Name,
            LobbySettings.ModeName,
            LobbySettings.Map.DisplayName,
            Session.PlayerCount,
            NetConfig.MAX_PLAYERS,
            Session.IsLobby,
            Identity.GamePort,
            NetConfig.PROTOCOL_VERSION,
            NetRegistry.SCHEMA_HASH);
    }
}
