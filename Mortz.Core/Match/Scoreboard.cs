using Mortz.Core.Match.WinConditions;

namespace Mortz.Core.Match;

/// <summary>
/// Match score state, owned by the server and rebuilt every match: per-player
/// rows plus per-team kill totals accumulated at kill time (never derived
/// from current members, so a leaver's kills stay counted). Win conditions
/// are predicates over one table, checked after every scored death.
/// </summary>
public sealed class Scoreboard
{
    public record struct Row(Team? Team, int Kills, int Deaths);

    public enum DeathKind
    {
        KILL,
        FALL,
        SUICIDE,
        TEAM_KILL,
        UNCREDITED,
    }

    /// <summary>A suicide's kill handed to an enemy; Kills is their tally after.</summary>
    public readonly record struct KillReward(int PeerId, int Kills);

    /// <summary>KillerId 0 is a death pit, the victim's own id a suicide.
    /// NearestEnemyId is 0 when nobody qualifies.</summary>
    public readonly record struct Death(int VictimId, int KillerId, int NearestEnemyId = 0);

    /// <summary>The complete result of applying one death; the single source of
    /// truth for attribution, nothing may re-classify the same death.</summary>
    public readonly record struct DeathResult(
        int KillerId,
        int VictimId,
        DeathKind Kind,
        Row? Killer,
        Row Victim,
        KillReward? Reward,
        TeamKills TeamKills,
        Victor? Winner)
    {
        public bool CreditedKill => Kind == DeathKind.KILL;
    }

    private readonly ModeRules _config;
    private readonly WinConditionStrategy _winCondition;
    private TeamKills _teamKills;
    // Sorted so scoreboard sync and winner scans are deterministic.
    private readonly SortedDictionary<int, Row> _rows = new();

    public IReadOnlyDictionary<int, Row> Rows => _rows;
    public TeamKills TeamKills => _teamKills;

    public Scoreboard(ModeRules config)
    {
        _config = config;
        _winCondition = WinConditionStrategy.Create(config.WinCondition);
    }

    public void AddPlayer(int peerId, Team? team = null)
    {
        if (team != null && !_config.Teams)
            throw new ArgumentException("Team assignment with the Teams rule off.", nameof(team));
        _rows[peerId] = new Row(team, 0, 0);
    }

    /// <summary>The row goes, the team total keeps what they scored.</summary>
    public void RemovePlayer(int peerId) => _rows.Remove(peerId);

    /// <summary>
    /// Scores one death and returns the winner if it decided the match.
    /// A death pit or the victim's own shell is a suicide: a death, never a
    /// kill, then whatever SuicidePenalty says. A killer who already left
    /// credits nobody. A teamkill awards nothing.
    /// </summary>
    public DeathResult? ScoreDeath(Death death)
    {
        if (!_rows.TryGetValue(death.VictimId, out Row victim))
            return null;
        _rows[death.VictimId] = victim with { Deaths = victim.Deaths + 1 };

        DeathKind kind;
        KillReward? reward = null;
        if (death.KillerId == 0)
        {
            kind = DeathKind.FALL;
            reward = SettleSuicide(death);
        }
        else if (death.KillerId == death.VictimId)
        {
            kind = DeathKind.SUICIDE;
            reward = SettleSuicide(death);
        }
        else if (!_rows.TryGetValue(death.KillerId, out Row killer))
        {
            kind = DeathKind.UNCREDITED;
        }
        else if (Teams.SameSide(killer.Team, victim.Team))
        {
            kind = DeathKind.TEAM_KILL;
        }
        else
        {
            kind = DeathKind.KILL;
            AddKills(death.KillerId, +1);
        }

        return new DeathResult(
            death.KillerId,
            death.VictimId,
            kind,
            _rows.TryGetValue(death.KillerId, out Row killerAfter) ? killerAfter : null,
            _rows[death.VictimId],
            reward,
            _teamKills,
            _winCondition.Resolve(Context()));
    }

    /// <summary>Non-null when the kill went to an enemy instead. At zero
    /// KILL_NO_NEGATIVE skips the subtraction entirely, so the team total
    /// stays the sum of its members.</summary>
    private KillReward? SettleSuicide(Death death)
    {
        switch (_config.SuicidePenalty)
        {
            case SuicidePenalty.KILL:
                AddKills(death.VictimId, -1);
                return null;
            case SuicidePenalty.KILL_NO_NEGATIVE:
                if (_rows[death.VictimId].Kills > 0)
                    AddKills(death.VictimId, -1);
                return null;
            case SuicidePenalty.REWARD_CLOSEST_ENEMY:
                if (!_rows.ContainsKey(death.NearestEnemyId))
                    return null;
                AddKills(death.NearestEnemyId, +1);
                return new KillReward(death.NearestEnemyId, _rows[death.NearestEnemyId].Kills);
            default:
                return null;
        }
    }

    /// <summary>Suicide penalties subtract from the team total too: the team
    /// score is the sum of what its members did, good and bad.</summary>
    private void AddKills(int peerId, int delta)
    {
        Row row = _rows[peerId];
        _rows[peerId] = row with { Kills = row.Kills + delta };
        if (row.Team is Team team)
            _teamKills = _teamKills.Add(team, delta);
    }

    /// <summary>Progress still needed by whoever is closest to winning.</summary>
    public int RemainingToWin() => Standing().Remaining;

    /// <summary>Leader is null while nobody has a meaningful lead; once
    /// Remaining hits zero they win.</summary>
    public readonly record struct MatchStanding(Victor? Leader, int Remaining);

    public MatchStanding Standing() => _winCondition.Standing(Context());

    private WinConditionContext Context() => new(_config, _rows, _teamKills);
}
