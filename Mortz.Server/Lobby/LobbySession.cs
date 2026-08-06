using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Players;

namespace Mortz.Server.Lobby;

/// <summary>Ready-up state for the current lobby. Per-player state lives in
/// lobby-lifetime cells on Player, a fresh key generation each lobby rather
/// than a partial reset. Toggling teams recomputes everyone; leaving doesn't
/// rebalance.</summary>
public sealed class LobbySession(LobbyStateKeys keys, bool teamsEnabled = false)
{
    // The wire speaks peer ids (swap targets), so this is the one translation
    // point back to Player.
    private readonly SortedDictionary<int, Player> _seated = [];
    private readonly LobbyStateKey<SeatState> _seatKey = keys.Claim<SeatState>();
    private readonly LobbyStateKey<SwapOfferState> _offerKey = keys.Claim<SwapOfferState>();
    private bool _teamsEnabled = teamsEnabled;

    public int Count => _seated.Count;

    public bool CanStart =>
        _seated.Count > 0 && _seated.Values.All(member => SeatOf(member).Ready);

    public IReadOnlyCollection<Player> Members => _seated.Values;

    public LobbyMember[] Players =>
        _seated.Values.Select(Snapshot).ToArray();

    public Team? TeamOf(Player player) => SeatOf(player).Team;

    /// <summary>Requires the player's lobby cells to be open already, so phase
    /// factories must not call this; seating waits for Begin.</summary>
    public void Add(Player player)
    {
        // Dealt before the newcomer is seated, so their unset seat never votes.
        Team? team = _teamsEnabled ? SmallestTeam() : null;
        _seated[player.PeerId] = player;
        SeatOf(player).Team = team;
    }

    /// <summary>The leaver's own cells die with Player.Close; this scrubs the
    /// references other players' offers still hold.</summary>
    public bool Remove(Player player)
    {
        if (!_seated.Remove(player.PeerId))
            return false;
        foreach (Player member in _seated.Values)
        {
            SwapOfferState offer = OfferOf(member);
            if (offer.Target == player)
                offer.Target = null;
        }
        return true;
    }

    public bool SetReady(Player player, bool ready)
    {
        if (!_seated.ContainsKey(player.PeerId))
            return false;
        SeatOf(player).Ready = ready;
        return true;
    }

    /// <summary>A player's own move onto a team, granted only while that team
    /// has a free slot.</summary>
    public bool TrySetTeam(Player player, Team team)
    {
        if (!_teamsEnabled || !_seated.ContainsKey(player.PeerId))
            return false;
        SeatState seat = SeatOf(player);
        if (seat.Team == team ||
            _seated.Values.Count(other => SeatOf(other).Team == team) >=
            TeamRules.SlotsPerTeam(Count))
        {
            return false;
        }
        seat.Team = team;
        PruneOffers();
        return true;
    }

    public SwapOffer[] SwapOffers => _seated.Values
        .Where(member => OfferOf(member).Target != null)
        .Select(member => new SwapOffer(member.PeerId, OfferOf(member).Target!.PeerId))
        .ToArray();

    /// <summary>One outstanding offer per player. Repeating an offer cancels
    /// it; offering to someone already offering back executes the swap.</summary>
    public SwapResult RequestSwap(Player from, int targetPeerId)
    {
        if (_seated.GetValueOrDefault(targetPeerId) is not Player to ||
            !CrossTeam(from, to))
        {
            return SwapResult.NONE;
        }
        SwapOfferState outgoing = OfferOf(from);
        if (outgoing.Target == to)
        {
            outgoing.Target = null;
            return SwapResult.CANCELLED;
        }
        SwapOfferState reciprocal = OfferOf(to);
        if (reciprocal.Target == from)
        {
            SeatState a = SeatOf(from);
            SeatState b = SeatOf(to);
            (a.Team, b.Team) = (b.Team, a.Team);
            reciprocal.Target = null;
            PruneOffers();
            return SwapResult.SWAPPED;
        }
        outgoing.Target = to;
        return SwapResult.OFFERED;
    }

    /// <summary>Follows the Teams rule; returns whether anything changed and
    /// therefore needs a broadcast.</summary>
    public bool SetTeamsEnabled(bool enabled)
    {
        if (enabled == _teamsEnabled)
            return false;
        _teamsEnabled = enabled;
        int next = 0;
        foreach (Player member in _seated.Values)
        {
            OfferOf(member).Target = null; // fresh assignment wipes manual arrangements
            SeatOf(member).Team = enabled ? Teams.Deal(next) : null;
            next++;
        }
        return _seated.Count > 0;
    }

    private bool CrossTeam(Player from, Player to) =>
        _teamsEnabled && from != to &&
        _seated.ContainsKey(from.PeerId) && _seated.ContainsKey(to.PeerId) &&
        SeatOf(from).Team is Team fromTeam && SeatOf(to).Team is Team toTeam &&
        fromTeam != toTeam;

    /// <summary>Offers only survive while their pair still spans both teams.</summary>
    private void PruneOffers()
    {
        foreach (Player member in _seated.Values)
        {
            SwapOfferState offer = OfferOf(member);
            if (offer.Target is Player target && !CrossTeam(member, target))
                offer.Target = null;
        }
    }

    private Team SmallestTeam() =>
        Teams.Smallest(_seated.Values.Select(member => SeatOf(member).Team));

    private SeatState SeatOf(Player player) => player.State(_seatKey);

    private SwapOfferState OfferOf(Player player) => player.State(_offerKey);

    private LobbyMember Snapshot(Player player)
    {
        SeatState seat = SeatOf(player);
        return new LobbyMember(player.PeerId, player.Name, seat.Ready, seat.Team);
    }
}
