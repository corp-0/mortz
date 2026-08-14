using Mortz.Core.Match.Events;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Events;

/// <summary>One shell fired by one killer, for counting same-shell kills.</summary>
public readonly record struct KillerShell(int KillerId, int ShellId);

/// <summary>Turns each frame's scored deaths into game events. First blood
/// arrives pre-judged; the rest come from the judge cells.</summary>
public sealed class GameEventJudge
{
    /// <summary>Streak a victim must carry for their death to be a shutdown.</summary>
    public const int SHUTDOWN_STREAK = 5;

    /// <summary>Same-shell kills needed for HOLY_SHIT.</summary>
    public const int HOLY_SHIT_KILLS = 3;

    public const float MULTI_KILL_WINDOW_SECONDS = 3f;
    public const int MULTI_KILL_WINDOW_TICKS =
        (int)(MULTI_KILL_WINDOW_SECONDS * SimConfig.TICK_RATE);

    /// <summary>Wipe progress goes stale when kills sit more than a window
    /// apart, so a wipe is a sustained run, not an all-match tally.</summary>
    public const float TEAM_WIPE_WINDOW_SECONDS = 15f;
    public const int TEAM_WIPE_WINDOW_TICKS =
        (int)(TEAM_WIPE_WINDOW_SECONDS * SimConfig.TICK_RATE);

    public const float SUICIDE_WINDOW_SECONDS = 30f;
    public const int SUICIDE_WINDOW_TICKS =
        (int)(SUICIDE_WINDOW_SECONDS * SimConfig.TICK_RATE);

    private readonly MatchStateKey<JudgeState> _key;
    private readonly IReadOnlyDictionary<int, Player> _seated;
    private readonly Func<Player, Team?> _teamOf;

    /// <summary>TeamOf is the live team lookup; only team wipes read it, and
    /// outside Teams mode it returns null for everyone.</summary>
    public GameEventJudge(MatchStateKeys keys, IReadOnlyDictionary<int, Player> seated,
        Func<Player, Team?> teamOf)
    {
        _key = keys.Claim<JudgeState>();
        _seated = seated;
        _teamOf = teamOf;
    }

    public JudgeState Of(Player player) => player.State(_key);

    public byte KillingSpreeMagnitude(Player player) => ClampToByte(Of(player).Streak);

    public List<Judgment> JudgeFrame(IReadOnlyList<ScoredKill> kills, int tick)
    {
        List<Judgment> events = new();
        // Only the peak is announced, or one shell's triple would come out as
        // "double kill, triple kill".
        Dictionary<int, int> chainPeaks = new();
        Dictionary<KillerShell, int> shellKills = new();
        foreach (ScoredKill kill in kills)
        {
            JudgeState victimState = Of(kill.Victim);
            int victimStreak = victimState.Streak;
            victimState.Streak = 0;
            victimState.KilledSinceDeath.Clear();

            int victimId = kill.Victim.PeerId;
            int killerId = kill.Killer?.PeerId ?? 0;
            if (kill.FirstBlood)
            {
                events.Add(new Judgment(GameEventKind.FIRST_BLOOD, killerId, victimId, 0));
            }

            if (kill.Kind is DeathKind.FALL or DeathKind.SUICIDE)
            {
                int count = CountSuicide(victimState, tick);
                SuicideCause cause = kill.Kind == DeathKind.FALL
                    ? SuicideCause.FALL
                    : SuicideCause.BLAST;
                events.Add(new Judgment(GameEventKind.SUICIDE, victimId, 0,
                    ClampToByte(count), (byte)cause));
                continue;
            }
            if (kill.Kind == DeathKind.TEAM_KILL)
            {
                events.Add(new Judgment(
                    GameEventKind.TEAM_KILL, killerId, victimId, 0));
                continue;
            }
            if (kill.Kind != DeathKind.KILL || kill.Killer is not Player killer)
                continue;

            JudgeState killerState = Of(killer);
            events.Add(new Judgment(
                GameEventKind.REGULAR_KILL, killerId, victimId, 0));
            killerState.SuicideCount = 0;
            if (kill.Owned)
                events.Add(new Judgment(GameEventKind.HUMILIATION, killerId, victimId, 0));

            if (victimStreak >= SHUTDOWN_STREAK)
            {
                events.Add(new Judgment(GameEventKind.SHUTDOWN, killerId, victimId,
                    ClampToByte(victimStreak)));
            }

            if (killerState.LastKillerId == victimId)
            {
                killerState.LastKillerId = 0;
                events.Add(new Judgment(GameEventKind.REVENGE, killerId, victimId, 0));
            }
            victimState.LastKillerId = killerId;

            if (CountWipeKill(killerState, killer, kill.Victim, tick))
            {
                events.Add(new Judgment(GameEventKind.TEAM_WIPE, killerId, 0, 0));
            }

            killerState.Streak++;

            if (Streaks.AnnouncementOrdinal(killerState.Streak) >= 0)
            {
                events.Add(new Judgment(GameEventKind.KILL_STREAK, killerId, 0,
                    ClampToByte(killerState.Streak)));
            }

            int chain = killerState.Chain > 0 &&
                        tick - killerState.ChainTick <= MULTI_KILL_WINDOW_TICKS
                ? killerState.Chain + 1
                : 1;
            killerState.Chain = chain;
            killerState.ChainTick = tick;
            if (chain > 1) chainPeaks[killerId] = chain;
            if (kill.ShellId < 0) continue;
            KillerShell shell = new(killerId, kill.ShellId);
            shellKills[shell] = shellKills.GetValueOrDefault(shell) + 1;
        }

        foreach ((int killerId, int chain) in chainPeaks)
        {
            events.Add(new Judgment(GameEventKind.MULTI_KILL, killerId, 0, ClampToByte(chain)));
        }
        foreach ((KillerShell shell, int count) in shellKills)
        {
            if (count >= HOLY_SHIT_KILLS)
                events.Add(new Judgment(GameEventKind.HOLY_SHIT, shell.KillerId, 0,
                    ClampToByte(count)));
        }

        return events;
    }

    /// <summary>The leaver's own state dies with their cells; this scrubs the
    /// references other players' cells still hold.</summary>
    public void PlayerLeft(Player leaver)
    {
        foreach (Player member in _seated.Values)
        {
            JudgeState state = Of(member);
            if (state.LastKillerId == leaver.PeerId)
                state.LastKillerId = 0;
            state.KilledSinceDeath.Remove(leaver.PeerId);
        }
    }

    private static int CountSuicide(JudgeState state, int tick)
    {
        state.SuicideCount = state.SuicideCount > 0 &&
                             tick - state.SuicideTick <= SUICIDE_WINDOW_TICKS
            ? state.SuicideCount + 1
            : 1;
        state.SuicideTick = tick;
        return state.SuicideCount;
    }

    /// <summary>Records the kill and returns true when the killer has now
    /// killed every current member of the enemy team, which resets the set.
    /// Unassigned players have no team, so they neither wipe nor need
    /// wiping.</summary>
    private bool CountWipeKill(JudgeState killerState, Player killer, Player victim, int tick)
    {
        if (_teamOf(killer) is not Team killerTeam ||
            _teamOf(victim) is not Team victimTeam ||
            killerTeam == victimTeam)
            return false;
        if (tick - killerState.WipeTick > TEAM_WIPE_WINDOW_TICKS)
            killerState.KilledSinceDeath.Clear();
        killerState.KilledSinceDeath.Add(victim.PeerId);
        killerState.WipeTick = tick;
        bool wipe = _seated.Values.All(member =>
            _teamOf(member) is not Team team ||
            team == killerTeam ||
            killerState.KilledSinceDeath.Contains(member.PeerId));
        if (wipe)
            killerState.KilledSinceDeath.Clear();
        return wipe;
    }

    private static byte ClampToByte(int value) => (byte)Math.Min(value, byte.MaxValue);
}
