namespace Mortz.Core.Match;

/// <summary>Turns each frame's scored deaths into game events. First blood
/// arrives pre-judged (the scoreboard claims it while scoring); the rest come
/// from streak, chain and same-shell state a judge keeps for one match.</summary>
public sealed class GameEventJudge
{
    /// <summary>ShellId is -1 when no shell was involved.</summary>
    public readonly record struct Kill(
        int KillerId,
        int VictimId,
        bool Credited,
        bool Owned,
        bool FirstBlood,
        int ShellId);

    /// <summary>VictimId is 0 when the event has no meaningful victim.</summary>
    public readonly record struct Judgment(
        GameEventKind Kind,
        int ActorId,
        int VictimId,
        byte Magnitude);

    /// <summary>Streak a victim must carry for their death to be a shutdown.</summary>
    public const int SHUTDOWN_STREAK = 5;

    /// <summary>Streak tiers announce at every multiple of this.</summary>
    public const int STREAK_STEP = 5;

    /// <summary>Same-shell kills needed for HOLY_SHIT.</summary>
    public const int HOLY_SHIT_KILLS = 3;

    private readonly KillStreakTracker _streaks = new();
    private readonly MultiKillTracker _chains = new();

    public List<Judgment> JudgeFrame(IReadOnlyList<Kill> kills, int tick)
    {
        List<Judgment> events = new();
        // Chains only rise within a frame, so the last write is the peak.
        // Announcing every step would turn one shell's triple into
        // "double kill, triple kill".
        Dictionary<int, int> chainPeaks = new();
        Dictionary<(int KillerId, int ShellId), int> shellKills = new();
        foreach (Kill kill in kills)
        {
            int victimStreak = _streaks.OnDeath(kill.VictimId);
            if (kill.FirstBlood)
                events.Add(new Judgment(GameEventKind.FIRST_BLOOD, kill.KillerId, kill.VictimId, 0));
            if (!kill.Credited)
                continue;
            if (kill.Owned)
                events.Add(new Judgment(GameEventKind.HUMILIATION, kill.KillerId, kill.VictimId, 0));
            if (victimStreak >= SHUTDOWN_STREAK)
                events.Add(new Judgment(GameEventKind.SHUTDOWN, kill.KillerId, kill.VictimId,
                    ClampToByte(victimStreak)));
            int streak = _streaks.OnKill(kill.KillerId);
            if (streak >= STREAK_STEP && streak % STREAK_STEP == 0)
                events.Add(new Judgment(GameEventKind.KILL_STREAK, kill.KillerId, 0,
                    ClampToByte(streak)));
            int chain = _chains.OnKill(kill.KillerId, tick);
            if (chain > 1)
                chainPeaks[kill.KillerId] = chain;
            if (kill.ShellId >= 0)
            {
                (int KillerId, int ShellId) shell = (kill.KillerId, kill.ShellId);
                shellKills[shell] = shellKills.GetValueOrDefault(shell) + 1;
            }
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
    }

    private static byte ClampToByte(int value) => (byte)Math.Min(value, byte.MaxValue);
}
