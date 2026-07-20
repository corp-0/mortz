using Godot;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Roster;

/// <summary>Connected-session roster, fed by whichever of the lobby and match
/// membership streams is active. Each update replaces the whole table, so
/// departures never leave stale entries behind.</summary>
public partial class ClientRoster : Node
{
    private readonly Dictionary<long, string> _names = [];

    public string NameOf(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";

    public override void _Ready()
    {
        LobbyStateMsg.Received += OnLobbyState;
        RosterMsg.Received += OnRoster;
    }

    public override void _ExitTree()
    {
        LobbyStateMsg.Received -= OnLobbyState;
        RosterMsg.Received -= OnRoster;
    }

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
