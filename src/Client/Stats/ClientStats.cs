using Godot;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Stats;

/// <summary>Connected-session tables of server-replicated per-player stats.
/// Each update carries the full table, so departed players drop out on the
/// next message.</summary>
public partial class ClientStats : Node
{
    private readonly Dictionary<int, int> _pings = [];
    private readonly Dictionary<int, int> _wins = [];

    public event Action? Changed;

    public int? PingMs(int peerId) => _pings.TryGetValue(peerId, out int ping) ? ping : null;
    public int Wins(int peerId) => _wins.GetValueOrDefault(peerId);

    public override void _Ready()
    {
        PingUpdateMsg.Received += OnPings;
        SessionStatsProtocol.WinsReceived += OnWins;
    }

    public override void _ExitTree()
    {
        PingUpdateMsg.Received -= OnPings;
        SessionStatsProtocol.WinsReceived -= OnWins;
    }

    private void OnPings(PingUpdateMsg message)
    {
        _pings.Clear();
        foreach (PeerPing ping in message.Pings)
        {
            _pings[ping.PeerId] = ping.PingMs;
        }
        Changed?.Invoke();
    }

    private void OnWins(IReadOnlyList<PeerWins> wins)
    {
        _wins.Clear();
        foreach (PeerWins win in wins)
        {
            _wins[win.PeerId] = win.Wins;
        }
        Changed?.Invoke();
    }
}
