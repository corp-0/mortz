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

        Assert.Empty(judge.JudgeFrame([Kill(99, VICTIM, credited: false)], tick: 5000));

        // The 4-streak is gone: four more kills reach 4 again, not 8.
        List<GameEventJudge.Judgment> events = RunStreak(judge, VICTIM, kills: 4, startTick: 9000);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.KILL_STREAK);
    }

    [Fact]
    public void KillStreakAnnouncesAtEveryTierOnly()
    {
        GameEventJudge judge = new();

        List<GameEventJudge.Judgment> events = RunStreak(judge, KILLER, kills: 11, startTick: 0);

        Assert.Equal([5, 10], events
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

        Assert.Empty(judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0));
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
             Kill(KILLER, KILLER, credited: false, shellId: 7)], tick: 0);

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

    private static GameEventJudge.Kill Kill(int killer, int victim, bool credited = true,
        bool owned = false, bool firstBlood = false, int shellId = -1) =>
        new(killer, victim, credited, owned, firstBlood, shellId);

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
