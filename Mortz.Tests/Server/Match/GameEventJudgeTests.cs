using Mortz.Core.Match;
using Mortz.Core.Match.Events;
using Mortz.Core.Match.Teams;
using Mortz.Server.Match;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class GameEventJudgeTests
{
    private const int KILLER = 1;
    private const int VICTIM = 2;
    private const int OTHER = 3;

    private readonly MatchCells _cells = new();
    private readonly Dictionary<int, Team?> _teams = [];
    private readonly GameEventJudge _judge;

    public GameEventJudgeTests() => _judge = new GameEventJudge(_cells.Keys, _cells.Seated,
        player => _teams.GetValueOrDefault(player.PeerId));

    [Fact]
    public void FirstBloodAndHumiliationPassThrough()
    {
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM, owned: true, firstBlood: true)], tick: 0);

        Assert.Contains(new Judgment(
            GameEventKind.FIRST_BLOOD, KILLER, VICTIM, 0), events);
        Assert.Contains(new Judgment(
            GameEventKind.HUMILIATION, KILLER, VICTIM, 0), events);
    }

    [Fact]
    public void UncreditedDeathEmitsNothingButStillResetsTheVictim()
    {
        RunStreak(VICTIM, kills: 4, startTick: 0);

        Assert.Empty(_judge.JudgeFrame(
            [Kill(0, VICTIM, kind: DeathKind.UNCREDITED)], tick: 5000));

        // The 4-streak is gone: five more kills announce the entry tier again,
        // not a continuation at 9.
        List<Judgment> events = RunStreak(VICTIM, kills: 5, startTick: 9000);
        Assert.Equal([5], events
            .Where(e => e.Kind == GameEventKind.KILL_STREAK)
            .Select(e => (int)e.Magnitude));
    }

    [Fact]
    public void KillStreakAnnouncesAtEntryThenEveryOddStreak()
    {
        List<Judgment> events = RunStreak(KILLER, kills: 13, startTick: 0);

        Assert.Equal([5, 7, 9, 11, 13], events
            .Where(e => e.Kind == GameEventKind.KILL_STREAK)
            .Select(e => (int)e.Magnitude));
    }

    [Fact]
    public void ShutdownCreditsTheKillerWithTheEndedStreak()
    {
        RunStreak(VICTIM, kills: 6, startTick: 0);

        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 50_000);

        Assert.Contains(new Judgment(
            GameEventKind.SHUTDOWN, KILLER, VICTIM, 6), events);
    }

    [Fact]
    public void NoShutdownBelowTheThreshold()
    {
        RunStreak(VICTIM, kills: 4, startTick: 0);

        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 50_000);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.SHUTDOWN);
    }

    [Fact]
    public void MultiKillChainsInsideTheWindowAndExpiresOutsideIt()
    {
        Assert.Contains(new Judgment(
            GameEventKind.REGULAR_KILL, KILLER, VICTIM, 0),
            _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0));
        List<Judgment> second = _judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: GameEventJudge.MULTI_KILL_WINDOW_TICKS);
        List<Judgment> expired = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: GameEventJudge.MULTI_KILL_WINDOW_TICKS * 3);

        Assert.Contains(new Judgment(
            GameEventKind.MULTI_KILL, KILLER, 0, 2), second);
        Assert.DoesNotContain(expired, e => e.Kind == GameEventKind.MULTI_KILL);
    }

    [Fact]
    public void SameFrameKillsAnnounceOnlyTheChainPeak()
    {
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM), Kill(KILLER, OTHER), Kill(KILLER, 4)], tick: 0);

        Assert.Equal([3], events
            .Where(e => e.Kind == GameEventKind.MULTI_KILL)
            .Select(e => (int)e.Magnitude));
    }

    [Fact]
    public void ThreeKillsWithOneShellAreHolyShit()
    {
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM, shellId: 7), Kill(KILLER, OTHER, shellId: 7),
             Kill(KILLER, 4, shellId: 7)], tick: 0);

        Assert.Contains(new Judgment(
            GameEventKind.HOLY_SHIT, KILLER, 0, 3), events);
    }

    [Fact]
    public void KillsSpreadOverTwoShellsAreNotHolyShit()
    {
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM, shellId: 7), Kill(KILLER, OTHER, shellId: 7),
             Kill(KILLER, 4, shellId: 8)], tick: 0);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HOLY_SHIT);
    }

    [Fact]
    public void TheShootersOwnSuicideDoesNotPadTheBodyCount()
    {
        // Shell 7 kills two enemies and its shooter: two credited kills only.
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM, shellId: 7), Kill(KILLER, OTHER, shellId: 7),
             Kill(KILLER, KILLER, shellId: 7,
                 kind: DeathKind.SUICIDE)], tick: 0);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HOLY_SHIT);
    }

    [Fact]
    public void LeaverLosesTheirStreak()
    {
        RunStreak(VICTIM, kills: 6, startTick: 0);

        // Their cells die with them; rejoining mints fresh ones.
        Player leaver = _cells.GetOrJoin(VICTIM);
        _cells.Leave(leaver);
        _judge.PlayerLeft(leaver);

        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 50_000);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.SHUTDOWN);
    }

    [Fact]
    public void LeaverGrudgesAreScrubbed()
    {
        _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);

        Player leaver = _cells.GetOrJoin(KILLER);
        _cells.Leave(leaver);
        _judge.PlayerLeft(leaver);

        // The killer rejoins as a fresh player; the old grudge must not fire.
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 50_000);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.REVENGE);
    }

    [Fact]
    public void RevengeFiresWhenTheGrudgeIsSettledAndOnlyThen()
    {
        _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);

        List<Judgment> revenge = _judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 50_000);
        // The grudge was cleared by the revenge; killing them again is not one.
        List<Judgment> again = _judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 100_000);

        Assert.Contains(new Judgment(
            GameEventKind.REVENGE, VICTIM, KILLER, 0), revenge);
        Assert.DoesNotContain(again, e => e.Kind == GameEventKind.REVENGE);
    }

    [Fact]
    public void TheGrudgeIsOverwrittenByANewerKiller()
    {
        _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);
        _judge.JudgeFrame([Kill(OTHER, VICTIM)], tick: 20_000);

        // Only the most recent killer counts as a grudge.
        List<Judgment> stale = _judge.JudgeFrame(
            [Kill(VICTIM, KILLER)], tick: 50_000);
        List<Judgment> fresh = _judge.JudgeFrame(
            [Kill(VICTIM, OTHER)], tick: 100_000);

        Assert.DoesNotContain(stale, e => e.Kind == GameEventKind.REVENGE);
        Assert.Contains(fresh, e => e.Kind == GameEventKind.REVENGE);
    }

    [Fact]
    public void KillingTheWholeEnemyRosterIsATeamWipe()
    {
        SeatTeams();

        List<Judgment> first = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: 0);
        List<Judgment> wipe = _judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: GameEventJudge.TEAM_WIPE_WINDOW_TICKS);

        Assert.DoesNotContain(first, e => e.Kind == GameEventKind.TEAM_WIPE);
        Assert.Contains(new Judgment(
            GameEventKind.TEAM_WIPE, KILLER, 0, 0), wipe);
    }

    [Fact]
    public void StaleKillsDropOutOfTheWipeWindow()
    {
        SeatTeams();
        _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);

        // The first kill went stale, so this one starts the run over.
        List<Judgment> late = _judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: GameEventJudge.TEAM_WIPE_WINDOW_TICKS * 2);
        List<Judgment> wipe = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM)], tick: GameEventJudge.TEAM_WIPE_WINDOW_TICKS * 2 + 100);

        Assert.DoesNotContain(late, e => e.Kind == GameEventKind.TEAM_WIPE);
        Assert.Contains(new Judgment(
            GameEventKind.TEAM_WIPE, KILLER, 0, 0), wipe);
    }

    [Fact]
    public void DyingResetsTheWipeProgress()
    {
        SeatTeams();
        _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 0);
        _judge.JudgeFrame([Kill(VICTIM, KILLER)], tick: 100);

        // Both kills sit inside the window, so only the death explains the miss.
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, OTHER)], tick: 200);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.TEAM_WIPE);
    }

    [Fact]
    public void SuicidesChainInsideTheWindowAndCarryTheirCause()
    {
        List<Judgment> first = _judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.BLAST)], tick: 0);
        List<Judgment> second = _judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.FALL)], tick: GameEventJudge.SUICIDE_WINDOW_TICKS);
        List<Judgment> expired = _judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.BLAST)], tick: GameEventJudge.SUICIDE_WINDOW_TICKS * 3);

        Assert.Equal([new Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.BLAST)], first);
        // The count aggregates across causes; each event still names its own.
        Assert.Equal([new Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 2, (byte)SuicideCause.FALL)], second);
        Assert.Equal([new Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.BLAST)], expired);
    }

    [Fact]
    public void ACreditedKillRedeemsTheSuicideCount()
    {
        _judge.JudgeFrame([Suicide(KILLER, SuicideCause.BLAST)], tick: 0);
        _judge.JudgeFrame([Kill(KILLER, VICTIM)], tick: 100);

        List<Judgment> events = _judge.JudgeFrame(
            [Suicide(KILLER, SuicideCause.BLAST)], tick: 200);

        Assert.Contains(new Judgment(
            GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.BLAST), events);
    }

    [Fact]
    public void RegularAndTeamKillsBecomePresentationEvents()
    {
        List<Judgment> events = _judge.JudgeFrame(
            [Kill(KILLER, VICTIM),
             Kill(KILLER, OTHER, kind: DeathKind.TEAM_KILL)], tick: 0);

        Assert.Contains(new Judgment(
            GameEventKind.REGULAR_KILL, KILLER, VICTIM, 0), events);
        Assert.Contains(new Judgment(
            GameEventKind.TEAM_KILL, KILLER, OTHER, 0), events);
    }

    /// <summary>Killer blue, victims red, all three seated up front: the wipe
    /// check walks the seated players, so late joins would shrink the enemy
    /// roster.</summary>
    private void SeatTeams()
    {
        _teams[KILLER] = Team.BLUE;
        _teams[VICTIM] = Team.RED;
        _teams[OTHER] = Team.RED;
        _cells.GetOrJoin(KILLER);
        _cells.GetOrJoin(VICTIM);
        _cells.GetOrJoin(OTHER);
    }

    /// <summary>The judge only reads Kind from the score; ids and tallies stay
    /// blank.</summary>
    private static DeathScore Score(DeathKind kind) =>
        new(0, 0, kind, null, default, null, default, null);

    private ScoredKill Suicide(int victim, SuicideCause cause)
    {
        Player player = _cells.GetOrJoin(victim);
        DeathKind kind = cause == SuicideCause.FALL ? DeathKind.FALL : DeathKind.SUICIDE;
        return new ScoredKill(player, player, Score(kind),
            Owned: false, FirstBlood: false, ShellId: -1);
    }

    private ScoredKill Kill(int killer, int victim,
        bool owned = false, bool firstBlood = false, int shellId = -1,
        DeathKind kind = DeathKind.KILL)
    {
        Player? credited = killer == 0 || kind == DeathKind.UNCREDITED
            ? null
            : _cells.GetOrJoin(killer);
        return new ScoredKill(credited, _cells.GetOrJoin(victim), Score(kind), owned,
            firstBlood, shellId);
    }

    /// <summary>One kill per frame, spaced far apart so no chains form.</summary>
    private List<Judgment> RunStreak(int killer, int kills, int startTick)
    {
        List<Judgment> events = new();
        for (int i = 0; i < kills; i++)
        {
            events.AddRange(_judge.JudgeFrame(
                [Kill(killer, 100 + i)],
                startTick + i * GameEventJudge.MULTI_KILL_WINDOW_TICKS * 2));
        }
        return events;
    }
}
