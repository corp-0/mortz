namespace Mortz.Core.Match;

/// <summary>Turns each frame's scored deaths into game events. First blood
/// arrives pre-judged; the rest come from state kept for one match.</summary>
public sealed class GameEventJudge
{
    /// <summary>ShellId is -1 when no shell was involved.</summary>
    public readonly record struct Kill(
        int KillerId,
        int VictimId,
        Scoreboard.DeathKind Kind,
        bool Owned,
        bool FirstBlood,
        int ShellId);

    /// <summary>VictimId is 0 when the event has no meaningful victim; Detail
    /// is 0 unless the kind has a use for it (SUICIDE stores the cause).</summary>
    public readonly record struct Judgment(
        GameEventKind Kind,
        int ActorId,
        int VictimId,
        byte Magnitude,
        byte Detail = 0);

    /// <summary>Streak a victim must carry for their death to be a shutdown.</summary>
    public const int SHUTDOWN_STREAK = 5;

    /// <summary>Streaks announce at this size, then at every odd streak.</summary>
    public const int STREAK_ENTRY = 5;

    public static int StreakAnnouncementOrdinal(int streak)
    {
        if (streak < STREAK_ENTRY || streak % 2 == 0)
            return -1;
        return (streak - STREAK_ENTRY) / 2;
    }

    /// <summary>Same-shell kills needed for HOLY_SHIT.</summary>
    public const int HOLY_SHIT_KILLS = 3;

    private readonly KillStreakTracker _streaks = new();
    private readonly MultiKillTracker _chains = new();
    private readonly RevengeTracker _grudges = new();
    private readonly TeamWipeTracker _wipes = new();
    private readonly SuicideTracker _suicides = new();

    /// <summary>Teams is the current peer-to-team map when playing Teams mode,
    /// null otherwise; only team wipes read it.</summary>
    public List<Judgment> JudgeFrame(IReadOnlyList<Kill> kills, int tick,
        IReadOnlyDictionary<int, byte>? teams = null)
    {
        List<Judgment> events = new();
        // Only the peak is announced, or one shell's triple would come out as
        // "double kill, triple kill".
        Dictionary<int, int> chainPeaks = new();
        Dictionary<(int KillerId, int ShellId), int> shellKills = new();
        foreach (Kill kill in kills)
        {
            int victimStreak = _streaks.OnDeath(kill.VictimId);
            _wipes.OnDeath(kill.VictimId);
            if (kill.FirstBlood)
            {
                events.Add(new Judgment(GameEventKind.FIRST_BLOOD, kill.KillerId, kill.VictimId, 0));
            }

            if (kill.Kind is Scoreboard.DeathKind.FALL or Scoreboard.DeathKind.SUICIDE)
            {
                int count = _suicides.OnSuicide(kill.VictimId, tick);
                SuicideCause cause = kill.Kind == Scoreboard.DeathKind.FALL
                    ? SuicideCause.FALL
                    : SuicideCause.BLAST;
                events.Add(new Judgment(GameEventKind.SUICIDE, kill.VictimId, 0,
                    ClampToByte(count), (byte)cause));
                continue;
            }
            if (kill.Kind == Scoreboard.DeathKind.TEAM_KILL)
            {
                events.Add(new Judgment(
                    GameEventKind.TEAM_KILL, kill.KillerId, kill.VictimId, 0));
                continue;
            }
            if (kill.Kind != Scoreboard.DeathKind.KILL)
                continue;

            events.Add(new Judgment(
                GameEventKind.REGULAR_KILL, kill.KillerId, kill.VictimId, 0));
            _suicides.OnCreditedKill(kill.KillerId);
            if (kill.Owned)
                events.Add(new Judgment(GameEventKind.HUMILIATION, kill.KillerId, kill.VictimId, 0));

            if (victimStreak >= SHUTDOWN_STREAK)
            {
                events.Add(new Judgment(GameEventKind.SHUTDOWN, kill.KillerId, kill.VictimId,
                    ClampToByte(victimStreak)));
            }

            if (_grudges.OnKill(kill.KillerId, kill.VictimId))
            {
                events.Add(new Judgment(GameEventKind.REVENGE, kill.KillerId, kill.VictimId, 0));
            }

            if (teams != null && _wipes.OnKill(kill.KillerId, kill.VictimId, tick, teams))
            {
                events.Add(new Judgment(GameEventKind.TEAM_WIPE, kill.KillerId, 0, 0));
            }

            int streak = _streaks.OnKill(kill.KillerId);

            if (StreakAnnouncementOrdinal(streak) >= 0)
            {
                events.Add(new Judgment(GameEventKind.KILL_STREAK, kill.KillerId, 0,
                    ClampToByte(streak)));
            }

            int chain = _chains.OnKill(kill.KillerId, tick);
            if (chain > 1) chainPeaks[kill.KillerId] = chain;
            if (kill.ShellId < 0) continue;
            (int KillerId, int ShellId) shell = (kill.KillerId, kill.ShellId);
            shellKills[shell] = shellKills.GetValueOrDefault(shell) + 1;
        }

        foreach ((int killerId, int chain) in chainPeaks)
        {
            events.Add(new Judgment(GameEventKind.MULTI_KILL, killerId, 0, ClampToByte(chain)));
        }
        foreach (((int killerId, _), int count) in shellKills)
        {
            if (count >= HOLY_SHIT_KILLS)
                events.Add(new Judgment(GameEventKind.HOLY_SHIT, killerId, 0, ClampToByte(count)));
        }

        return events;
    }

    public void RemovePlayer(int peerId)
    {
        _streaks.RemovePlayer(peerId);
        _chains.RemovePlayer(peerId);
        _grudges.RemovePlayer(peerId);
        _wipes.RemovePlayer(peerId);
        _suicides.RemovePlayer(peerId);
    }

    private static byte ClampToByte(int value) => (byte)Math.Min(value, byte.MaxValue);
}
