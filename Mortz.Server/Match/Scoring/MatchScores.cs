using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Server.Match.Scoring.SuicidePenalties;
using Mortz.Server.Match.Scoring.WinConditions;
using Mortz.Server.Players;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Server.Match.Scoring;

/// <summary>Rows live in each seated player's score cell; team kill totals
/// accumulate here at kill time, so a leaver's kills stay counted.</summary>
public sealed class MatchScores(
    ModeRules config,
    MatchStateKeys keys,
    IReadOnlyDictionary<int, Player> seated)
{
    private readonly MatchStateKey<ScoreState> _key = keys.Claim<ScoreState>();
    private readonly WinConditionStrategy _winCondition = WinConditionStrategy.Create(config.WinCondition);
    private readonly SuicidePenaltyStrategy _suicidePenalty = SuicidePenaltyStrategy.Create(config.SuicidePenalty);
    private TeamKills _teamKills;

    public TeamKills TeamKills => _teamKills;

    public ScoreState Of(Player player) => player.State(_key);

    public void Seat(Player player, Team? team)
    {
        if (team != null && !config.Teams)
            throw new ArgumentException("Team assignment with the Teams rule off.", nameof(team));
        player.State(_key).Team = team;
    }

    /// <summary>Scores one death, returns the winner if it decided the match.
    /// A death pit or the victim's own shell is a suicide: a death, never a
    /// kill, then whatever SuicidePenalty says. A killer who already left
    /// credits nobody; a teamkill awards nothing.</summary>
    public DeathScore ScoreDeath(int killerId, Player victim, Player? killer,
        Player? nearestEnemy)
    {
        ScoreState victimRow = Of(victim);
        victimRow.Deaths++;

        DeathKind kind;
        KillReward? reward = null;
        if (killerId == 0)
        {
            kind = DeathKind.FALL;
            reward = _suicidePenalty.Apply(victim, nearestEnemy, this);
        }
        else if (killerId == victim.PeerId)
        {
            kind = DeathKind.SUICIDE;
            reward = _suicidePenalty.Apply(victim, nearestEnemy, this);
        }
        else if (killer == null)
        {
            kind = DeathKind.UNCREDITED;
        }
        else if (Teams.SameSide(Of(killer).Team, victimRow.Team))
        {
            kind = DeathKind.TEAM_KILL;
        }
        else
        {
            kind = DeathKind.KILL;
            AddKills(killer, +1);
        }

        return new DeathScore(
            killerId,
            victim.PeerId,
            kind,
            killer == null ? null : Of(killer).Snapshot,
            victimRow.Snapshot,
            reward,
            _teamKills,
            _winCondition.Resolve(Context()));
    }

    public int AddKills(Player player, int delta)
    {
        ScoreState row = Of(player);
        row.Kills += delta;
        if (row.Team is Team team)
            _teamKills = _teamKills.Add(team, delta);
        return row.Kills;
    }

    /// <summary>Progress still needed by whoever is closest to winning.</summary>
    public int RemainingToWin() => Standing().Remaining;

    public MatchStanding Standing() => _winCondition.Standing(Context());

    /// <summary>Snapshot of every seated player's row, in ascending peer id.</summary>
    public IReadOnlyList<SeatedScore> Rows()
    {
        List<SeatedScore> rows = new(seated.Count);
        foreach (Player player in seated.Values)
        {
            rows.Add(new SeatedScore(player, Of(player).Snapshot));
        }
        return rows;
    }

    private WinConditionContext Context() => new(config, Rows(), _teamKills);
}
