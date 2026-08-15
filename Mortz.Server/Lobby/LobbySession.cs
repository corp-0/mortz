using System.Collections.Immutable;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Lobby;

/// <summary>The authoritative participant state for one lobby lifetime.</summary>
public sealed class LobbySession(bool teamsEnabled = false)
{
    private readonly SortedDictionary<int, LobbyParticipant> _participants = [];
    private bool _teamsEnabled = teamsEnabled;

    public int Count => _participants.Count;

    public bool CanStart =>
        _participants.Count > 0 && _participants.Values.All(participant => participant.Ready);

    public LobbySnapshot Snapshot => Freeze();

    public SeatAssignment[] Seats =>
    [
        .. _participants.Values.Select(participant =>
            new SeatAssignment(participant.Player, participant.Team))
    ];

    public LobbyUpdate Initialize(IEnumerable<Player> players)
    {
        foreach (Player player in players)
        {
            AddParticipant(player);
        }

        return Updated(new LobbyChange.Initialized(_participants.Count));
    }

    public LobbyUpdate? Join(Player player)
    {
        if (_participants.ContainsKey(player.PeerId))
            return null;

        AddParticipant(player);
        return Updated(new LobbyChange.Joined(player.PeerId, player.Name));
    }

    public LobbyUpdate? Leave(Player player)
    {
        if (!_participants.Remove(player.PeerId))
            return null;

        foreach (LobbyParticipant participant in _participants.Values)
        {
            if (participant.SwapTargetPeerId == player.PeerId)
                participant.SwapTargetPeerId = null;
        }

        return Updated(new LobbyChange.Left(player.PeerId));
    }

    public LobbyUpdate? SetReady(Player player, bool ready)
    {
        if (!_participants.TryGetValue(player.PeerId, out LobbyParticipant? participant) ||
            participant.Ready == ready)
        {
            return null;
        }

        participant.Ready = ready;
        return Updated(new LobbyChange.ReadinessChanged(player.PeerId, ready));
    }

    /// <summary>A player's own move onto a team, granted only while that team has a free slot.</summary>
    public LobbyUpdate? TrySetTeam(Player player, Team team)
    {
        if (!_teamsEnabled ||
            !_participants.TryGetValue(player.PeerId, out LobbyParticipant? participant) ||
            participant.Team == team ||
            _participants.Values.Count(other => other.Team == team) >=
            TeamRules.SlotsPerTeam(Count))
        {
            return null;
        }

        participant.Team = team;
        PruneOffers();
        return Updated(new LobbyChange.TeamChanged(player.PeerId, team));
    }

    /// <summary>One outstanding offer per player. Repeating one cancels it; a reciprocal offer swaps.</summary>
    public LobbyUpdate? RequestSwap(Player from, int targetPeerId)
    {
        if (!_participants.TryGetValue(from.PeerId, out LobbyParticipant? source) ||
            !_participants.TryGetValue(targetPeerId, out LobbyParticipant? target) ||
            !CrossTeam(source, target))
        {
            return null;
        }

        if (source.SwapTargetPeerId == targetPeerId)
        {
            source.SwapTargetPeerId = null;
            return Updated(new LobbyChange.SwapCancelled(from.PeerId, targetPeerId));
        }

        if (target.SwapTargetPeerId == from.PeerId)
        {
            (source.Team, target.Team) = (target.Team, source.Team);
            target.SwapTargetPeerId = null;
            PruneOffers();
            return Updated(new LobbyChange.TeamsSwapped(from.PeerId, targetPeerId));
        }

        source.SwapTargetPeerId = targetPeerId;
        return Updated(new LobbyChange.SwapOffered(from.PeerId, targetPeerId));
    }

    /// <summary>Follows the Teams rule and returns no update when the replicated state is unchanged.</summary>
    public LobbyUpdate? SetTeamsEnabled(bool enabled)
    {
        if (enabled == _teamsEnabled)
            return null;

        _teamsEnabled = enabled;
        int next = 0;
        foreach (LobbyParticipant participant in _participants.Values)
        {
            participant.SwapTargetPeerId = null;
            participant.Team = enabled ? Teams.Deal(next) : null;
            next++;
        }

        return _participants.Count == 0
            ? null
            : Updated(new LobbyChange.TeamsRuleChanged(enabled));
    }

    private void AddParticipant(Player player)
    {
        Team? team = _teamsEnabled ? SmallestTeam() : null;
        _participants.Add(player.PeerId, new LobbyParticipant
        {
            Player = player,
            Team = team,
        });
    }

    private bool CrossTeam(LobbyParticipant from, LobbyParticipant to) =>
        _teamsEnabled && from != to &&
        from.Team is Team fromTeam && to.Team is Team toTeam && fromTeam != toTeam;

    private void PruneOffers()
    {
        foreach (LobbyParticipant participant in _participants.Values)
        {
            if (participant.SwapTargetPeerId is int targetPeerId &&
                (!_participants.TryGetValue(targetPeerId, out LobbyParticipant? target) ||
                 !CrossTeam(participant, target)))
            {
                participant.SwapTargetPeerId = null;
            }
        }
    }

    private Team SmallestTeam() =>
        Teams.Smallest(_participants.Values.Select(participant => participant.Team));

    private LobbyUpdate Updated(LobbyChange change)
    {
        LobbySnapshot snapshot = Freeze();
        return new LobbyUpdate(snapshot, change, snapshot.CanStart);
    }

    private LobbySnapshot Freeze() => new(
        [.. _participants.Values.Select(participant => new LobbyMember(
            participant.Player.PeerId,
            participant.Player.Name,
            participant.Ready,
            participant.Team))],
        [.. _participants.Values
            .Where(participant => participant.SwapTargetPeerId.HasValue)
            .Select(participant => new SwapOffer(
                participant.Player.PeerId,
                participant.SwapTargetPeerId!.Value))],
        CanStart);
}

public sealed class LobbyParticipant
{
    public required Player Player { get; init; }
    public bool Ready { get; set; }
    public Team? Team { get; set; }
    public int? SwapTargetPeerId { get; set; }
}

public sealed record LobbySnapshot(
    ImmutableArray<LobbyMember> Members,
    ImmutableArray<SwapOffer> Offers,
    bool CanStart);

public sealed record LobbyUpdate(
    LobbySnapshot Snapshot,
    LobbyChange Change,
    bool CanStart);

public abstract record LobbyChange
{
    public sealed record Initialized(int Count) : LobbyChange;
    public sealed record Joined(int PeerId, string Name) : LobbyChange;
    public sealed record Left(int PeerId) : LobbyChange;
    public sealed record ReadinessChanged(int PeerId, bool Ready) : LobbyChange;
    public sealed record TeamChanged(int PeerId, Team Team) : LobbyChange;
    public sealed record SwapOffered(int PeerId, int TargetPeerId) : LobbyChange;
    public sealed record SwapCancelled(int PeerId, int TargetPeerId) : LobbyChange;
    public sealed record TeamsSwapped(int PeerId, int TargetPeerId) : LobbyChange;
    public sealed record TeamsRuleChanged(bool Enabled) : LobbyChange;
}
