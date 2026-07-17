using Mortz.Core.Net.Messages;

namespace Mortz.Client.Stats;

/// <summary>Connected-session tables of server-replicated per-player stats.
/// Each update carries the full table, so departed players drop out on the
/// next message.</summary>
public sealed class ClientStatsSession : IDisposable
{
    private readonly Dictionary<long, int> _pings = new();
    private readonly Dictionary<long, int> _wins = new();
    private bool _subscribed;

    public event Action? Changed;

    public int? PingMs(long peerId) => _pings.TryGetValue(peerId, out int ping) ? ping : null;
    public int Wins(long peerId) => _wins.GetValueOrDefault(peerId);

    public void Subscribe()
    {
        if (_subscribed)
            return;
        PingUpdateMsg.Received += OnPingUpdate;
        SessionWinsMsg.Received += OnSessionWins;
        _subscribed = true;
    }

    public void Clear()
    {
        _pings.Clear();
        _wins.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            PingUpdateMsg.Received -= OnPingUpdate;
            SessionWinsMsg.Received -= OnSessionWins;
            _subscribed = false;
        }
        Clear();
    }

    private void OnPingUpdate(PingUpdateMsg message) =>
        Replace(_pings, message.PeerIds, message.PingsMs);

    private void OnSessionWins(SessionWinsMsg message) =>
        Replace(_wins, message.PeerIds, message.Wins);

    private void Replace(Dictionary<long, int> table, long[] peerIds, int[] values)
    {
        table.Clear();
        int count = Math.Min(peerIds.Length, values.Length);
        for (int i = 0; i < count; i++)
            table[peerIds[i]] = values[i];
        Changed?.Invoke();
    }
}
