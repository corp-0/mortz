using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>Owns match scoring and turns ordered deaths into scored eliminations.</summary>
public class ScoringStep(
    ModeRules rules,
    MatchStateKeys keys,
    IReadOnlyDictionary<int, Player> seated) : IMatchStep
{
    private readonly MatchScores _scores = new(rules, keys, seated);
    private bool _firstBloodClaimed;

    public TeamKills TeamKills => _scores.TeamKills;

    public void Seat(Player player, Team? team) => _scores.Seat(player, team);

    public PlayerScore ScoreOf(Player player) => _scores.Of(player).Snapshot;

    public Team? TeamOf(Player player) => _scores.Of(player).Team;

    public IReadOnlyList<SeatedScore> Rows() => _scores.Rows();

    public MatchStanding Standing() => _scores.Standing();

    public void Advance(MatchTick tick)
    {
        List<ScoredKill> eliminations = [];
        WinningScore? winningScore = null;
        foreach (Death death in tick.Deaths)
        {
            if (ScoreDeath(tick.Match, death) is not ScoredKill elimination)
                continue;
            eliminations.Add(elimination);
            if (elimination.Score.Winner == null)
                continue;
            winningScore = new WinningScore(death, elimination);
            break;
        }

        tick.SetScoring(eliminations, _scores.Standing(), winningScore);
    }

    private ScoredKill? ScoreDeath(MatchContext match, Death death)
    {
        if (match.Stage != MatchStage.PLAYING)
            return null;
        if (match.SeatedPlayers.GetValueOrDefault(death.PeerId) is not Player victim)
            return null;
        Player? killer = ResolveKiller(match, death, victim);
        DeathScore result = _scores.ScoreDeath(
            death.KillerId, victim, killer, NearestEnemy(match, death));
        bool firstBlood = !_firstBloodClaimed && result.CreditedKill;
        if (firstBlood)
            _firstBloodClaimed = true;
        return new ScoredKill(
            killer, victim, result, death.Owned, firstBlood, death.ShellId);
    }

    private static Player? ResolveKiller(MatchContext match, Death death, Player victim)
    {
        if (death.KillerId == 0)
            return null;
        if (death.KillerId == death.PeerId)
            return victim;
        return match.SeatedPlayers.GetValueOrDefault(death.KillerId);
    }

    /// <summary>The living enemy nearest the death spot, null when there is none.</summary>
    private static Player? NearestEnemy(MatchContext match, Death death)
    {
        SimWorld world = match.World;
        Team? victimTeam = world.Players.TryGetValue(death.PeerId, out PlayerState victim)
            ? victim.Team
            : null;
        Player? closest = null;
        float best = float.MaxValue;
        foreach ((int peerId, PlayerState player) in world.Players)
        {
            if (peerId == death.PeerId || player.RespawnTicks > 0)
                continue;
            if (Teams.SameSide(victimTeam, player.Team))
                continue;
            float distance = (player.Position - death.Position).LengthSquared();
            if (distance >= best)
                continue;
            best = distance;
            closest = match.SeatedPlayers[peerId];
        }
        return closest;
    }
}
