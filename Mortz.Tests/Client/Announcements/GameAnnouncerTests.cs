using Mortz.Client.Announcements;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client.Announcements;

public class GameAnnouncerTests
{
    private const int KILLER = 7;
    private const int VICTIM = 8;
    private const int STRANGER = 99;

    [Fact]
    public void FirstBloodIsGlobal()
    {
        List<GameAnnouncer.Cue> cues = GameAnnouncer.Plan(
            [Event(GameEventKind.FIRST_BLOOD)], localId: STRANGER);

        Assert.Equal([GameAnnouncer.Cue.FIRST_BLOOD], cues);
    }

    [Fact]
    public void HumiliationPlaysLobbyWide()
    {
        Announcement[] batch = [Event(GameEventKind.HUMILIATION)];

        Assert.Equal([GameAnnouncer.Cue.HUMILIATION], GameAnnouncer.Plan(batch, KILLER));
        Assert.Equal([GameAnnouncer.Cue.HUMILIATION], GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void AnyOtherLineSilencesHumiliation()
    {
        IReadOnlyList<Announcement> batch = Prepare(
            Msg(GameEventKind.HUMILIATION), Msg(GameEventKind.FIRST_BLOOD));

        Assert.Equal([GameAnnouncer.Cue.FIRST_BLOOD], GameAnnouncer.Plan(batch, KILLER));
    }

    [Fact]
    public void MultiKillSwallowsTheStreakLine()
    {
        IReadOnlyList<Announcement> batch = Prepare(
            Msg(GameEventKind.KILL_STREAK, magnitude: 5),
            Msg(GameEventKind.MULTI_KILL, magnitude: 2));

        Assert.Equal([GameAnnouncer.Cue.DOUBLE_KILL], GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void HolyShitChainsIntoTheMultiKillLine()
    {
        IReadOnlyList<Announcement> batch = Prepare(
            Msg(GameEventKind.MULTI_KILL, magnitude: 3),
            Msg(GameEventKind.HOLY_SHIT, magnitude: 3));

        Assert.Equal([GameAnnouncer.Cue.HOLY_SHIT, GameAnnouncer.Cue.TRIPLE_KILL],
            GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void HolyShitCompositeSpeaksTheChainNotTheBodyCount()
    {
        // A 3-kill shell landing mid chain: the composite honors the x5.
        IReadOnlyList<Announcement> batch = Prepare(
            Msg(GameEventKind.MULTI_KILL, magnitude: 5),
            Msg(GameEventKind.HOLY_SHIT, magnitude: 3));

        Assert.Equal([GameAnnouncer.Cue.HOLY_SHIT, GameAnnouncer.Cue.ULTRA_KILL],
            GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void StreakTiersMapToTheirCues()
    {
        (byte Magnitude, GameAnnouncer.Cue Expected)[] tiers =
        [
            (5, GameAnnouncer.Cue.BLOODLUST),
            (7, GameAnnouncer.Cue.PUNISHMENT),
            (9, GameAnnouncer.Cue.DOMINATING),
            (11, GameAnnouncer.Cue.MACHINE_GOD),
            (13, GameAnnouncer.Cue.PSYCHO),
            (23, GameAnnouncer.Cue.PSYCHO),
        ];
        Assert.All(tiers, tier => Assert.Equal([tier.Expected], GameAnnouncer.Plan(
            [Event(GameEventKind.KILL_STREAK, tier.Magnitude)], STRANGER)));
    }

    [Fact]
    public void MultiKillTiersMapToTheirCues()
    {
        (byte Magnitude, GameAnnouncer.Cue Expected)[] tiers =
        [
            (4, GameAnnouncer.Cue.OVERKILL),
            (5, GameAnnouncer.Cue.ULTRA_KILL),
            (6, GameAnnouncer.Cue.MASSACRE),
            (7, GameAnnouncer.Cue.CARNAGE),
            (12, GameAnnouncer.Cue.CARNAGE),
        ];
        Assert.All(tiers, tier => Assert.Equal([tier.Expected], GameAnnouncer.Plan(
            [Event(GameEventKind.MULTI_KILL, tier.Magnitude)], STRANGER)));
    }

    [Fact]
    public void TeamWipeIsGlobal()
    {
        Assert.Equal([GameAnnouncer.Cue.TEAM_WIPE], GameAnnouncer.Plan(
            [Event(GameEventKind.TEAM_WIPE)], STRANGER));
    }

    [Fact]
    public void SuicideMocksOnlyTheSuiciderAndOnlyWhenItBecomesAHabit()
    {
        Announcement[] third = [Event(GameEventKind.SUICIDE, magnitude: 3)];

        Assert.Equal([GameAnnouncer.Cue.SUICIDE], GameAnnouncer.Plan(third, KILLER));
        Assert.Empty(GameAnnouncer.Plan(third, STRANGER));
        Assert.Empty(GameAnnouncer.Plan(
            [Event(GameEventKind.SUICIDE, magnitude: 2)], KILLER));
    }

    [Fact]
    public void RevengeIsChatOnly()
    {
        Assert.Empty(GameAnnouncer.Plan([Event(GameEventKind.REVENGE)], KILLER));
    }

    private static IReadOnlyList<Announcement> Prepare(params GameEventMsg[] events) =>
        AnnouncementDirector.Describe(events, static _ => "p", static _ => 0);

    private static Announcement Event(GameEventKind kind, byte magnitude = 0) =>
        new(kind, new Combatant(KILLER, "p1", 0), new Combatant(VICTIM, "p2", 0), magnitude);

    private static GameEventMsg Msg(GameEventKind kind, byte magnitude = 0) =>
        new(kind, KILLER, VICTIM, magnitude);
}
