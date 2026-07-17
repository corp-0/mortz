using Mortz.Core.Net.Messages;

namespace Mortz.Client.Roster;

/// <summary>Connected-session roster, fed by whichever of the lobby and match
/// membership streams is active. Each update replaces the whole table, so
/// departures never leave stale entries behind.</summary>
public partial class ClientRoster : SessionScopedNode
{
    private readonly Dictionary<long, string> _names = [];

    public string NameOf(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";

    protected override void OnServiceReady()
    {
        LobbyStateMsg.Received += OnLobbyState;
        RosterMsg.Received += OnRoster;
    }

    protected override void OnServiceExit()
    {
        LobbyStateMsg.Received -= OnLobbyState;
        RosterMsg.Received -= OnRoster;
        _names.Clear();
    }

    protected override void OnSessionBoundary() => _names.Clear();

    private void OnLobbyState(LobbyStateMsg message) =>
        Update(message.PeerIds, message.Names);

    private void OnRoster(RosterMsg message) => Update(message.PeerIds, message.Names);

    private void Update(long[] peerIds, string[] names)
    {
        _names.Clear();
        int count = Math.Min(peerIds.Length, names.Length);
        for (int i = 0; i < count; i++)
        {
            _names[peerIds[i]] = names[i];
        }
    }
}
