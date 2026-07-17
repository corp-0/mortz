namespace Mortz.Server;

internal readonly record struct LobbyPlayer(long PeerId, bool Ready, byte Team);

/// <summary>Ready-up state for the current lobby. It owns no match resources,
/// so returning from a match is a fresh session rather than a partial reset.
/// Team assignment is reactive: toggling teams on deals everyone out fresh,
/// joiners land on the smallest team, leavers reshuffle nobody, and toggling
/// off clears every assignment.</summary>
internal sealed class LobbySession
{
    private readonly SortedDictionary<long, (bool Ready, byte Team)> _players = new();
    private bool _teamsEnabled;

    public int Count => _players.Count;
    public bool CanStart => _players.Count > 0 && _players.Values.All(player => player.Ready);
    public IReadOnlyList<LobbyPlayer> Players =>
        _players.Select(pair => new LobbyPlayer(pair.Key, pair.Value.Ready, pair.Value.Team))
            .ToArray();

    public void Add(long peerId) =>
        _players[peerId] = (false, _teamsEnabled ? SmallestTeam() : (byte)0);

    public bool Remove(long peerId) => _players.Remove(peerId);

    public bool SetReady(long peerId, bool ready)
    {
        if (!_players.TryGetValue(peerId, out (bool Ready, byte Team) player))
            return false;
        _players[peerId] = (ready, player.Team);
        return true;
    }

    /// <summary>Follows the Teams rule; returns whether anything changed and
    /// therefore needs a broadcast.</summary>
    public bool SetTeamsEnabled(bool enabled)
    {
        if (enabled == _teamsEnabled)
            return false;
        _teamsEnabled = enabled;
        byte next = 0;
        foreach (long peerId in _players.Keys.ToArray())
        {
            byte team = enabled ? (byte)(next % 2 + 1) : (byte)0;
            _players[peerId] = (_players[peerId].Ready, team);
            next++;
        }
        return _players.Count > 0;
    }

    private byte SmallestTeam()
    {
        int one = _players.Values.Count(player => player.Team == 1);
        int two = _players.Values.Count(player => player.Team == 2);
        return (byte)(one <= two ? 1 : 2);
    }

    public static LobbySession For(IEnumerable<long> peerIds, bool teamsEnabled = false)
    {
        LobbySession lobby = new() { _teamsEnabled = teamsEnabled };
        foreach (long peerId in peerIds)
            lobby.Add(peerId);
        return lobby;
    }
}
