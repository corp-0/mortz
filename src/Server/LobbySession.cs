using Mortz.Core.Match;

namespace Mortz.Server;

internal readonly record struct LobbyPlayer(long PeerId, bool Ready, byte Team);

internal enum SwapResult
{
    NONE,
    OFFERED,
    CANCELLED,
    SWAPPED,
}

/// <summary>Ready-up state for the current lobby. It owns no match resources,
/// so returning from a match is a fresh session rather than a partial reset.
/// Team assignment is reactive: toggling teams on deals everyone out fresh,
/// joiners land on the smallest team, leavers reshuffle nobody, and toggling
/// off clears every assignment.</summary>
internal sealed class LobbySession
{
    private readonly SortedDictionary<long, (bool Ready, byte Team)> _players = new();
    // Pending swap offers, one outgoing per player.
    private readonly SortedDictionary<long, long> _offers = new();
    private bool _teamsEnabled;

    public int Count => _players.Count;
    public bool CanStart => _players.Count > 0 && _players.Values.All(player => player.Ready);
    public IReadOnlyList<LobbyPlayer> Players =>
        _players.Select(pair => new LobbyPlayer(pair.Key, pair.Value.Ready, pair.Value.Team))
            .ToArray();

    public void Add(long peerId) =>
        _players[peerId] = (false, _teamsEnabled ? SmallestTeam() : (byte)0);

    public bool Remove(long peerId)
    {
        if (!_players.Remove(peerId))
            return false;
        _offers.Remove(peerId);
        PruneOffers();
        return true;
    }

    public bool SetReady(long peerId, bool ready)
    {
        if (!_players.TryGetValue(peerId, out (bool Ready, byte Team) player))
            return false;
        _players[peerId] = (ready, player.Team);
        return true;
    }

    /// <summary>A player's own move onto a team, granted only while that team
    /// has a free slot.</summary>
    public bool TrySetTeam(long peerId, byte team)
    {
        if (!_teamsEnabled || team is not (1 or 2) ||
            !_players.TryGetValue(peerId, out (bool Ready, byte Team) player) ||
            player.Team == team ||
            _players.Values.Count(other => other.Team == team) >= TeamRules.SlotsPerTeam(Count))
        {
            return false;
        }
        _players[peerId] = (player.Ready, team);
        PruneOffers();
        return true;
    }

    public IReadOnlyList<(long From, long To)> SwapOffers =>
        _offers.Select(pair => (pair.Key, pair.Value)).ToArray();

    /// <summary>One outstanding offer per player. Repeating an offer cancels
    /// it; offering to someone already offering back executes the swap.</summary>
    public SwapResult RequestSwap(long from, long to)
    {
        if (!CrossTeam(from, to))
            return SwapResult.NONE;
        if (_offers.TryGetValue(from, out long current) && current == to)
        {
            _offers.Remove(from);
            return SwapResult.CANCELLED;
        }
        if (_offers.TryGetValue(to, out long reciprocal) && reciprocal == from)
        {
            (bool Ready, byte Team) a = _players[from];
            (bool Ready, byte Team) b = _players[to];
            _players[from] = (a.Ready, b.Team);
            _players[to] = (b.Ready, a.Team);
            _offers.Remove(to);
            PruneOffers();
            return SwapResult.SWAPPED;
        }
        _offers[from] = to;
        return SwapResult.OFFERED;
    }

    private bool CrossTeam(long from, long to) =>
        _teamsEnabled && from != to &&
        _players.TryGetValue(from, out (bool Ready, byte Team) a) &&
        _players.TryGetValue(to, out (bool Ready, byte Team) b) &&
        a.Team != 0 && b.Team != 0 && a.Team != b.Team;

    /// <summary>Offers only survive while their pair still spans both teams.</summary>
    private void PruneOffers()
    {
        foreach ((long from, long to) in _offers.ToArray())
            if (!CrossTeam(from, to))
                _offers.Remove(from);
    }

    /// <summary>Follows the Teams rule; returns whether anything changed and
    /// therefore needs a broadcast.</summary>
    public bool SetTeamsEnabled(bool enabled)
    {
        if (enabled == _teamsEnabled)
            return false;
        _teamsEnabled = enabled;
        _offers.Clear(); // fresh assignment wipes manual arrangements
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
