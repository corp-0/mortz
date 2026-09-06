using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Sim;
using Mortz.Protocol.Net.Stats;

namespace Mortz.Server;

/// <summary>Gates phase traffic until bootstrap is acknowledged, retaining only the latest snapshot.</summary>
public sealed class ReadyLink(IServerTransport transport) : IServerLink
{
    private sealed class PeerState(int generation, ulong deadline)
    {
        public int Generation { get; } = generation;
        public ulong Deadline { get; } = deadline;
        public Queue<Action> Pending { get; } = new();
        public Action? Snapshot { get; set; }
        public bool Ready { get; set; }
        public bool Disconnecting { get; set; }
    }

    private readonly Dictionary<int, PeerState> _peers = [];
    private int _generation;

    public void BeginLoading(int peerId, int generation, ulong nowMs)
    {
        _generation = generation;
        _peers[peerId] = new PeerState(generation, nowMs + NetConfig.PHASE_READY_TIMEOUT_MS);
    }

    public bool Ready(int peerId, int generation)
    {
        if (!_peers.TryGetValue(peerId, out PeerState? peer) || peer.Ready || peer.Disconnecting ||
            peer.Generation != generation)
            return false;
        peer.Ready = true;
        while (peer.Pending.TryDequeue(out Action? send))
        {
            send();
        }
        peer.Snapshot?.Invoke();
        peer.Snapshot = null;
        return true;
    }

    public void Remove(int peerId) => _peers.Remove(peerId);

    public void DisconnectExpired(ulong nowMs)
    {
        foreach ((int peerId, PeerState peer) in _peers.ToArray())
        {
            if (!peer.Ready && !peer.Disconnecting && nowMs >= peer.Deadline)
            {
                transport.Disconnect(peerId, "phase readiness timed out");
                peer.Disconnecting = true;
                peer.Pending.Clear();
                peer.Snapshot = null;
            }
        }
    }

    public void Send<TMsg>(int peerId, in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        TMsg copy = TMsg.InMatch(message, _generation);
        Deliver(peerId, () => transport.Send(peerId, in copy), IsBootstrap<TMsg>());
    }

    public void Broadcast<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        TMsg copy = TMsg.InMatch(message, _generation);
        if (_peers.Count == 0 || _peers.Values.All(peer => peer.Ready))
        {
            transport.Broadcast(in copy);
            return;
        }
        foreach (int peerId in _peers.Keys.ToArray())
        {
            Deliver(peerId, () => transport.Send(peerId, in copy), IsBootstrap<TMsg>());
        }
    }

    public void Disconnect(int peerId, string reason)
    {
        _peers.Remove(peerId);
        transport.Disconnect(peerId, reason);
    }

    public int BroadcastSnapshot(Func<int, byte[]> dataFor, Func<int, int> ackFor)
    {
        int bytes = 0;
        foreach (int peerId in _peers.Keys.ToArray())
        {
            byte[] data = dataFor(peerId);
            int ack = ackFor(peerId);
            bytes += data.Length + sizeof(int);
            DeliverSnapshot(peerId, data, ack);
        }
        return bytes;
    }

    public int SendSnapshot(int peerId, byte[] data, int ack)
    {
        DeliverSnapshot(peerId, data, ack);
        return data.Length + sizeof(int);
    }

    private void DeliverSnapshot(int peerId, byte[] data, int ack)
    {
        if (_peers.TryGetValue(peerId, out PeerState? peer))
        {
            if (peer.Disconnecting)
                return;
            if (!peer.Ready)
            {
                peer.Snapshot = () => transport.SendSnapshot(peerId, data, ack);
                return;
            }
        }
        transport.SendSnapshot(peerId, data, ack);
    }

    public PeerPing[] PeerPings() => transport.PeerPings();

    public WireStats PopWireStats() => transport.PopWireStats();

    private void Deliver(int peerId, Action send, bool bootstrap)
    {
        if (!_peers.TryGetValue(peerId, out PeerState? peer))
        {
            send();
            return;
        }
        if (peer.Disconnecting)
            return;
        if (peer.Ready || bootstrap)
        {
            send();
            return;
        }
        if (peer.Pending.Count >= NetConfig.MAX_LOADING_MESSAGES)
        {
            transport.Disconnect(peerId, "phase readiness queue overflow");
            peer.Disconnecting = true;
            peer.Pending.Clear();
            peer.Snapshot = null;
            return;
        }
        peer.Pending.Enqueue(send);
    }

    private static bool IsBootstrap<TMsg>() =>
        typeof(TMsg) == typeof(LobbyLoadMsg) ||
        typeof(TMsg) == typeof(MatchLoadMsg) ||
        typeof(TMsg) == typeof(TerrainChunkMsg);
}
