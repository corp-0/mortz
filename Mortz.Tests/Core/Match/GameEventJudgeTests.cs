using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class GameEventJudgeTests
{
    private const int KILLER = 1;
    private const int VICTIM = 2;
    private const int OTHER = 3;

    [Fact]
    public void FirstBloodAndHumiliationPassThrough()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM, owned: true, firstBlood: true)], tick: 0);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.FIRST_BLOOD, KILLER, VICTIM, 0), events);
        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.HUMILIATION, KILLER, VICTIM, 0), events);
    }

    [Fact]
    public void UncreditedDeathEmitsNothingButStillResetsTheVictim()
    {
        GameEventJudge judge = new();
        RunStreak(judge, VICTIM, kills: 4, startTick: 0);

        Assert.Empty(judge.JudgeFrame(
            [Kill(99, VICTIM, kind: Scoreboard.DeathKind.UNCREDITED)], tick: 5000));

        // The 4-streak is gone: five more kills announce the entry tier again,
        // not a continuation at 9.
        List<GameEventJudge.Judgment> events = RunStreak(judge, VICTIM, kills: 5, startTick: 9000);
        Assert.Equal([5], events
            .Where(e => e.Kind == GameEventKind.KILL_STREAK)
            .Select(e => (int)e.Magnitude));
    }

    [Fact]
    public void KillStreakAnnouncesAtEntryThenEveryOddStreak()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = RunStreak(judge, KILLER, kills: 13, startTick: 0);

        Assert.Equal([5, 7, 9, 11, 13], events
            .Where(e => e.Kind == GameEventKind.KILL_STREAK)
            .Select(e => (int)e.Magnitude));
    }

    [Fact]
    public void ShutdownCreditsTheKillerWithTheEndedStreak()
    {
        GameEventJudge judge = new();
        RunStreak(judge, VICTIM, kills: 6, startTick: 0);

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 50_000);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.SHUTDOWN, KILLER, VICTIM, 6), events);
    }

    [Fact]
    public void NoShutdownBelowTheThreshold()
    {
        GameEventJudge judge = new();
        RunStreak(judge, VICTIM, kills: 4, startTick: 0);

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 50_000);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.SHUTDOWN);
    }

    [Fact]
    public void MultiKillChainsInsideTheWindowAndExpiresOutsideIt()
    {
        GameEventJudge judge = new();

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.REGULAR_KILL, KILLER, VICTIM, 0),
            judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0));
        List<GameEventJudge.Judgment> second = judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: MultiKillTracker.WINDOW_TICKS);
        List<GameEventJudge.Judgment> expired = judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: MultiKillTracker.WINDOW_TICKS * 3);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.MULTI_KILL, KILLER, 0, 2), second);
        Assert.DoesNotContain(expired, e => e.Kind == GameEventKind.MULTI_KILL);
    }

    [Fact]
    public void SameFrameKillsAnnounceOnlyTheChainPeak()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM), Kill(KILLER, OTHER), Kill(KILLER, 4)], tick: 0);

        Assert.Equal([3], events
            .Where(e => e.Kind == GameEventKind.MULTI_KILL)
            .Select(e => (int)e.Magnitude));
    }

    [Fact]
    public void ThreeKillsWithOneShellAreHolyShit()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM, shellId: 7), Kill(KILLER, OTHER, shellId: 7),
             Kill(KILLER, 4, shellId: 7)], tick: 0);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.HOLY_SHIT, KILLER, 0, 3), events);
    }

    [Fact]
    public void KillsSpreadOverTwoShellsAreNotHolyShit()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM, shellId: 7), Kill(KILLER, OTHER, shellId: 7),
             Kill(KILLER, 4, shellId: 8)], tick: 0);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HOLY_SHIT);
    }

    [Fact]
    public void TheShootersOwnSuicideDoesNotPadTheBodyCount()
    {
        GameEventJudge judge = new();

        // Shell 7 kills two enemies and its shooter: two credited kills only.
        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM, shellId: 7), Kill(KILLER, OTHER, shellId: 7),
             Kill(KILLER, KILLER, shellId: 7,
                 kind: Scoreboard.DeathKind.SUICIDE)], tick: 0);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HOLY_SHIT);
    }

    [Fact]
    public void RemovedPlayerLosesTheirStreak()
    {
        GameEventJudge judge = new();
        RunStreak(judge, VICTIM, kills: 6, startTick: 0);

        judge.RemovePlayer(VICTIM);
        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 50_000);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.SHUTDOWN);
    }

    [Fact]
    public void RevengeFiresWhenTheGrudgeIsSettledAndOnlyThen()
    {
        GameEventJudge judge = new();
        judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);

        List<GameEventJudge.Judgment> revenge = judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 50_000);
        // The grudge was cleared by the revenge; killing them again is not one.
        List<GameEventJudge.Judgment> again = judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 100_000);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.REVENGE, VICTIM, KILLER, 0), revenge);
        Assert.DoesNotContain(again, e => e.Kind == GameEventKind.REVENGE);
    }

    [Fact]
    public void TheGrudgeIsOverwrittenByANewerKiller()
    {
        GameEventJudge judge = new();
        judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);
        judge.JudgeFrame([Kill(OTHER, VICTIM)], tick: 20_000);

        // Only the most recent killer counts as a grudge.
        List<GameEventJudge.Judgment> stale = judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 50_000);
        List<GameEventJudge.Judgment> fresh = judge.JudgeFrame(
            [Kill(VICTIM, OTHER)], tick: 100_000);

        Assert.DoesNotContain(stale, e => e.Kind == GameEventKind.REVENGE);
        Assert.Contains(fresh, e => e.Kind == GameEventKind.REVENGE);
    }

    [Fact]
    public void KillingTheWholeEnemyRosterIsATeamWipe()
    {
        GameEventJudge judge = new();
        Dictionary<int, byte> teams = new() { [KILLER] = 1, [VICTIM] = 2, [OTHER] = 2 };

        List<GameEventJudge.Judgment> first = judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 0, teams);
        List<GameEventJudge.Judgment> wipe = judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: TeamWipeTracker.WINDOW_TICKS, teams);

        Assert.DoesNotContain(first, e => e.Kind == GameEventKind.TEAM_WIPE);
        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.TEAM_WIPE, KILLER, 0, 0), wipe);
    }

    [Fact]
    public void StaleKillsDropOutOfTheWipeWindow()
    {
        GameEventJudge judge = new();
        Dictionary<int, byte> teams = new() { [KILLER] = 1, [VICTIM] = 2, [OTHER] = 2 };
        judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0, teams);

        // The first kill went stale, so this one starts the run over.
        List<GameEventJudge.Judgment> late = judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: TeamWipeTracker.WINDOW_TICKS * 2, teams);
        List<GameEventJudge.Judgment> wipe = judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: TeamWipeTracker.WINDOW_TICKS * 2 + 100, teams);

        Assert.DoesNotContain(late, e => e.Kind == GameEventKind.TEAM_WIPE);
        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.TEAM_WIPE, KILLER, 0, 0), wipe);
    }

    [Fact]
    public void DyingResetsTheWipeProgress()
    {
        GameEventJudge judge = new();
        Dictionary<int, byte> teams = new() { [KILLER] = 1, [VICTIM] = 2, [OTHER] = 2 };
        judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0, teams);
        judge.JudgeFrame([Kill(VICTIM, KILLER)], tick: 100, teams);

        // Both kills sit inside the window, so only the death explains the miss.
        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: 200, teams);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.TEAM_WIPE);
    }

    [Fact]
    public void SuicidesChainInsideTheWindowAndCarryTheirCause()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> first = judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.BLAST)], tick: 0);
        List<GameEventJudge.Judgment> second = judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.FALL)], tick: SuicideTracker.WINDOW_TICKS);
        List<GameEventJudge.Judgment> expired = judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.BLAST)], tick: SuicideTracker.WINDOW_TICKS * 3);

        Assert.Equal([new GameEventJudge.Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.BLAST)], first);
        // The count aggregates across causes; each event still names its own.
        Assert.Equal([new GameEventJudge.Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 2, (byte)SuicideCause.FALL)], second);
        Assert.Equal([new GameEventJudge.Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.BLAST)], expired);
    }

    [Fact]
    public void ACreditedKillRedeemsTheSuicideCount()
    {
        GameEventJudge judge = new();
        judge.JudgeFrame([Suicide(KILLER, SuicideCause.BLAST)], tick: 0);
        judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 100);

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.BLAST)], tick: 200);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.BLAST), events);
    }

    [Fact]
    public void RegularAndTeamKillsBecomePresentationEvents()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = judge.JudgeFrame(
            [Kill(KILLER, VICTIM),
             Kill(KILLER, OTHER, kind: Scoreboard.DeathKind.TEAM_KILL)], tick: 0);

        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.REGULAR_KILL, KILLER, VICTIM, 0), events);
        Assert.Contains(new GameEventJudge.Judgment(
            GameEventKind.TEAM_KILL, KILLER, OTHER, 0), events);
    }

    private static GameEventJudge.Kill Suicide(int victim, SuicideCause cause) =>
        new(victim, victim,
            cause == SuicideCause.FALL
                ? Scoreboard.DeathKind.FALL
                : Scoreboard.DeathKind.SUICIDE,
            Owned: false, FirstBlood: false, ShellId: -1);

    private static GameEventJudge.Kill Kill(int killer, int victim,
        bool owned = false, bool firstBlood = false, int shellId = -1,
        Scoreboard.DeathKind kind = Scoreboard.DeathKind.KILL) =>
        new(killer, victim, kind, owned, firstBlood, shellId);

    /// <summary>One kill per frame, spaced far apart so no chains form.</summary>
    private static List<GameEventJudge.Judgment> RunStreak(
        GameEventJudge judge, int killer, int kills, int startTick)
    {
        List<GameEventJudge.Judgment> events = new();
        for (int i = 0; i < kills; i++)
        {
            events.AddRange(judge.JudgeFrame(
                [Kill(killer, 100 + i)], startTick + i * MultiKillTracker.WINDOW_TICKS * 2));
        }
        return events;
    }
}
