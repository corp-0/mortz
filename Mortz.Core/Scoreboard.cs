namespace Mortz.Core;

/// <summary>
/// Match score state, owned by the server and rebuilt every match. Two
/// tables: per-player rows (any scoreboard needs them, whatever the mode) and
/// per-team kill totals accumulated at kill time, never derived from current
/// members, so a leaver's kills stay counted for their team. Win conditions
/// are predicates over one of the tables, checked after every scored death.
/// </summary>
public sealed class Scoreboard
{
    public record struct Row(byte TeamId, int Kills, int Deaths);

    /// <summary>Id is a team id when ByTeam, a peer id otherwise.</summary>
    public readonly record struct MatchWinner(bool ByTeam, int Id);

    private readonly MatchConfig _config;
    // 1-based by TeamId ([0] never read); two teams in v1, sized here only.
    private readonly int[] _teamKills = new int[3];
    // Sorted so scoreboard sync and winner scans are deterministic.
    private readonly SortedDictionary<int, Row> _rows = new();

    public IReadOnlyDictionary<int, Row> Rows => _rows;
    public int TeamKills(byte teamId) => _teamKills[teamId];

    public Scoreboard(MatchConfig config) => _config = config;

    public void AddPlayer(int peerId, byte teamId) => _rows[peerId] = new Row(teamId, 0, 0);

    /// <summary>The row goes, the team total keeps what they scored.</summary>
    public void RemovePlayer(int peerId) => _rows.Remove(peerId);

    /// <summary>
    /// Scores one death and returns the winner if it decided the match.
    /// KillerId 0 (death pit) or the victim themselves is a suicide: a death,
    /// never a kill, minus one kill when the penalty is on. A killer who
    /// already left credits nobody. A teamkill awards nothing.
    /// </summary>
    public MatchWinner? RecordDeath(int victimId, int killerId)
    {
        if (!_rows.TryGetValue(victimId, out Row victim))
            return null;
        _rows[victimId] = victim with { Deaths = victim.Deaths + 1 };

        if (killerId == 0 || killerId == victimId)
        {
            if (_config.SuicidePenalty)
                AddKills(victimId, -1);
        }
        else if (_rows.TryGetValue(killerId, out Row killer) &&
                 !(_config.Teams && killer.TeamId != 0 && killer.TeamId == victim.TeamId))
        {
            AddKills(killerId, +1);
        }
        return CheckWinner();
    }

    /// <summary>Suicide penalties subtract from the team total too: the team
    /// score is the sum of what its members did, good and bad.</summary>
    private void AddKills(int peerId, int delta)
    {
        Row row = _rows[peerId];
        _rows[peerId] = row with { Kills = row.Kills + delta };
        if (row.TeamId != 0)
            _teamKills[row.TeamId] += delta;
    }

    /// <summary>TEAM_KILLS with teams off silently plays as PLAYER_KILLS (a
    /// team of one is the same thing), keeping the stored condition intact
    /// for when teams come back on.</summary>
    private MatchWinner? CheckWinner()
    {
        if (_config.Teams && _config.WinCondition == WinCondition.TEAM_KILLS)
        {
            for (byte team = 1; team < _teamKills.Length; team++)
                if (_teamKills[team] >= _config.KillTarget)
                    return new MatchWinner(true, team);
            return null;
        }
        foreach ((int peerId, Row row) in _rows)
            if (row.Kills >= _config.KillTarget)
                return new MatchWinner(false, peerId);
        return null;
    }
}
