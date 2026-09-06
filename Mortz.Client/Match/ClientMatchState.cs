using System.Collections.ObjectModel;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;

namespace Mortz.Client.Match;

public readonly record struct PlayerScore(int Kills, int Deaths);

public sealed record MatchScoreSnapshot(
    IReadOnlyDictionary<int, PlayerScore> Players,
    TeamKills TeamKills)
{
    public static MatchScoreSnapshot Empty { get; } =
        new(new ReadOnlyDictionary<int, PlayerScore>(new Dictionary<int, PlayerScore>()), default);

    public int Kills(int peerId) => Players.GetValueOrDefault(peerId).Kills;
    public int Deaths(int peerId) => Players.GetValueOrDefault(peerId).Deaths;
}

public readonly record struct MatchScoreRow(int PeerId, int Kills, int Deaths);

public readonly record struct MatchScorePatch(
    int KillerId,
    int VictimId,
    bool Suicide,
    int KillerKills,
    int VictimDeaths,
    int RewardedId,
    int RewardedKills,
    TeamKills TeamKills);

/// <summary>The durable state for one client-side match generation.</summary>
public sealed class ClientMatchState
{
    private bool _open = true;

    public ClientMatchState(int generation, MatchParticipation initialParticipation)
    {
        Generation = generation;
        if (!TryApplyParticipation(generation, initialParticipation))
            throw new ArgumentException("Invalid initial participation.", nameof(initialParticipation));
    }

    public int Generation { get; }
    public MatchParticipation Participation { get; private set; }
    public MatchPoint? MatchPoint { get; private set; }
    public Victor? Winner { get; private set; }
    public MatchScoreSnapshot Scores { get; private set; } = MatchScoreSnapshot.Empty;

    public event Action<MatchParticipation>? ParticipationChanged;
    public event Action<MatchPoint?>? MatchPointChanged;
    public event Action<Victor?>? WinnerChanged;
    public event Action<MatchScoreSnapshot>? ScoresChanged;

    public bool TryApplyParticipation(int generation, MatchParticipation participation)
    {
        if (!Accepts(generation) || !participation.IsValid || Participation == participation)
            return false;
        Participation = participation;
        ParticipationChanged?.Invoke(participation);
        return true;
    }

    public bool TryApplyMatchPoint(int generation, MatchPoint? matchPoint)
    {
        if (!Accepts(generation) || MatchPoint == matchPoint)
            return false;
        MatchPoint = matchPoint;
        MatchPointChanged?.Invoke(matchPoint);
        return true;
    }

    public bool TryApplyWinner(int generation, Victor winner)
    {
        ArgumentNullException.ThrowIfNull(winner);
        if (!Accepts(generation) || Winner != null)
            return false;
        Winner = winner;
        WinnerChanged?.Invoke(winner);
        return true;
    }

    public bool TryReplaceScores(
        int generation,
        IReadOnlyList<MatchScoreRow> rows,
        TeamKills teamKills)
    {
        if (!Accepts(generation))
            return false;
        Dictionary<int, PlayerScore> players = new(rows.Count);
        foreach (MatchScoreRow row in rows)
        {
            if (row.PeerId <= 0 || row.Deaths < 0 ||
                !players.TryAdd(row.PeerId, new PlayerScore(row.Kills, row.Deaths)))
            {
                return false;
            }
        }
        PublishScores(players, teamKills);
        return true;
    }

    public bool TryPatchScores(int generation, MatchScorePatch patch)
    {
        if (!Accepts(generation) || patch.VictimId <= 0 || patch.VictimDeaths < 0 ||
            patch.KillerId < 0 || patch.RewardedId < 0)
        {
            return false;
        }

        Dictionary<int, PlayerScore> players = new(Scores.Players);
        PlayerScore victim = players.GetValueOrDefault(patch.VictimId);
        victim = victim with { Deaths = patch.VictimDeaths };
        if (patch.Suicide)
            victim = victim with { Kills = patch.KillerKills };
        players[patch.VictimId] = victim;

        if (!patch.Suicide && patch.KillerId != 0)
        {
            PlayerScore killer = players.GetValueOrDefault(patch.KillerId);
            players[patch.KillerId] = killer with { Kills = patch.KillerKills };
        }
        if (patch.RewardedId != 0)
        {
            PlayerScore rewarded = players.GetValueOrDefault(patch.RewardedId);
            players[patch.RewardedId] = rewarded with { Kills = patch.RewardedKills };
        }

        PublishScores(players, patch.TeamKills);
        return true;
    }

    /// <summary>Stops callbacks retained by a retiring scene from changing this match.</summary>
    public void Close() => _open = false;

    private bool Accepts(int generation) => _open && generation == Generation;

    private void PublishScores(Dictionary<int, PlayerScore> players, TeamKills teamKills)
    {
        Scores = new MatchScoreSnapshot(
            new ReadOnlyDictionary<int, PlayerScore>(players), teamKills);
        ScoresChanged?.Invoke(Scores);
    }
}
