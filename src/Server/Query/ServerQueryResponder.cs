using System.Buffers.Binary;
using Godot;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Platform;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Server.Query;

[GlobalClass]
public partial class ServerQueryResponder : Node
{
    private static readonly ILogger _log = MortzLog.For("server");
    private readonly A2SResponder _responder = new();
    private PacketPeerUdp? _socket;
    private Func<ServerInfo>? _describe;
    private IServerPacketRouter? _router;

    public int BoundQueryPort { get; private set; } = -1;

    public bool Start(int queryPort, Func<ServerInfo> describe, string bindAddress = "*", IServerPacketRouter? router = null)
    {
        if (_socket != null)
        {
            throw new InvalidOperationException("Query responder already started.");
        }
        ProcessMode = ProcessModeEnum.Always;
        PacketPeerUdp socket = new();
        Error error = socket.Bind(queryPort, bindAddress);
        if (error != Error.Ok)
        {
            _log.Error("query port {Port} unavailable: {Error}; direct joining remains ready", queryPort, error);
            socket.Dispose();
            return false;
        }
        _socket = socket;
        _describe = describe;
        _router = router;
        BoundQueryPort = queryPort;
        _log.Information("answering A2S queries on port {Port}", queryPort);
        return true;
    }

    public void Stop()
    {
        _socket?.Close();
        _socket?.Dispose();
        _socket = null;
        _describe = null;
        _router = null;
        BoundQueryPort = -1;
    }

    public override void _ExitTree() => Stop();

    public override void _Process(double delta)
    {
        if (_socket is not PacketPeerUdp socket || _describe == null)
        {
            return;
        }
        ulong now = Time.GetTicksMsec();
        for (int i = 0; i < 32 && socket.GetAvailablePacketCount() > 0; i++)
        {
            byte[] packet = socket.GetPacket();
            string source = socket.GetPacketIP();
            int port = socket.GetPacketPort();
            if (!ServerQueryProtocol.TryDecodeRequest(packet, out _, out _))
            {
                if (packet.Length is >= 5 and <= 16384 && BinaryPrimitives.ReadInt32LittleEndian(packet) == -1)
                {
                    _router?.HandleIncoming(packet, source, port);
                }
                continue;
            }
            foreach (byte[] response in _responder.Respond(packet, source, port, now, _describe()))
            {
                socket.SetDestAddress(source, port);
                socket.PutPacket(response);
            }
        }
        while (_router?.TakeOutgoing() is ServerDatagram outgoing)
        {
            socket.SetDestAddress(outgoing.Address, outgoing.Port);
            socket.PutPacket(outgoing.Data);
        }
    }
}
