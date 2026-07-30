using Mortz.Core.Match;
using Mortz.Core.Net;

namespace Mortz.Server.Lobby;

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
    private readonly SortedDictionary<long, LobbyMember> _seats = new();
    // Pending swap offers, one outgoing per player.
    private readonly SortedDictionary<long, long> _offers = new();
    private bool _teamsEnabled;

    public int Count => _seats.Count;
    public bool CanStart => _seats.Count > 0 && _seats.Values.All(seat => seat.Ready);

    // A copy, not a projection: SetTeamsEnabled rewrites while callers iterate.
    public IReadOnlyList<LobbyMember> Players => _seats.Values.ToArray();

    public void Add(long peerId, string name) =>
        _seats[peerId] = new LobbyMember(peerId, name, false,
            _teamsEnabled ? SmallestTeam() : null);

    public bool Remove(long peerId)
    {
        if (!_seats.Remove(peerId))
            return false;
        _offers.Remove(peerId);
        PruneOffers();
        return true;
    }

    public bool SetReady(long peerId, bool ready)
    {
        if (!_seats.TryGetValue(peerId, out LobbyMember seat))
            return false;
        _seats[peerId] = seat.WithReady(ready);
        return true;
    }

    /// <summary>A player's own move onto a team, granted only while that team
    /// has a free slot.</summary>
    public bool TrySetTeam(long peerId, Team team)
    {
        if (!_teamsEnabled ||
            !_seats.TryGetValue(peerId, out LobbyMember seat) ||
            seat.Team == team ||
            _seats.Values.Count(other => other.Team == team) >= TeamRules.SlotsPerTeam(Count))
        {
            return false;
        }
        _seats[peerId] = seat.OnTeam(team);
        PruneOffers();
        return true;
    }

    public IReadOnlyList<SwapOffer> SwapOffers =>
        _offers.Select(pair => new SwapOffer(pair.Key, pair.Value)).ToArray();

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
            LobbyMember a = _seats[from];
            LobbyMember b = _seats[to];
            _seats[from] = a.OnTeam(b.Team);
            _seats[to] = b.OnTeam(a.Team);
            _offers.Remove(to);
            PruneOffers();
            return SwapResult.SWAPPED;
        }
        _offers[from] = to;
        return SwapResult.OFFERED;
    }

    private bool CrossTeam(long from, long to) =>
        _teamsEnabled && from != to &&
        _seats.TryGetValue(from, out LobbyMember a) &&
        _seats.TryGetValue(to, out LobbyMember b) &&
        a.Team is Team fromTeam && b.Team is Team toTeam && fromTeam != toTeam;

    /// <summary>Offers only survive while their pair still spans both teams.</summary>
    private void PruneOffers()
    {
        foreach ((long from, long to) in _offers.ToArray())
        {
            if (!CrossTeam(from, to))
                _offers.Remove(from);
        }
    }

    /// <summary>Follows the Teams rule; returns whether anything changed and
    /// therefore needs a broadcast.</summary>
    public bool SetTeamsEnabled(bool enabled)
    {
        if (enabled == _teamsEnabled)
            return false;
        _teamsEnabled = enabled;
        _offers.Clear(); // fresh assignment wipes manual arrangements
        int next = 0;
        foreach (long peerId in _seats.Keys.ToArray())
        {
            Team? team = enabled ? Teams.Deal(next) : null;
            _seats[peerId] = _seats[peerId].OnTeam(team);
            next++;
        }
        return _seats.Count > 0;
    }

    private Team SmallestTeam() =>
        Teams.Smallest(_seats.Values.Select(seat => seat.Team));

    public static LobbySession For(IReadOnlyDictionary<long, string> players,
        bool teamsEnabled = false)
    {
        LobbySession lobby = new() { _teamsEnabled = teamsEnabled };
        foreach ((long peerId, string name) in players)
        {
            lobby.Add(peerId, name);
        }
        return lobby;
    }
}
