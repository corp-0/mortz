using Mortz.Client.Announcements;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client.Announcements;

public class GameAnnouncerTests
{
    private const long KILLER = 7;
    private const long VICTIM = 8;
    private const long STRANGER = 99;

    [Fact]
    public void FirstBloodIsGlobal()
    {
        List<GameAnnouncer.Cue> cues = GameAnnouncer.Plan(
            [Event(GameEventKind.FIRST_BLOOD)], localId: STRANGER);

        Assert.Equal([GameAnnouncer.Cue.FIRST_BLOOD], cues);
    }

    [Fact]
    public void HumiliationPlaysForKillerAndVictimOnly()
    {
        GameEventMsg[] batch = [Event(GameEventKind.HUMILIATION)];

        Assert.Equal([GameAnnouncer.Cue.HUMILIATION], GameAnnouncer.Plan(batch, KILLER));
        Assert.Equal([GameAnnouncer.Cue.HUMILIATION], GameAnnouncer.Plan(batch, VICTIM));
        Assert.Empty(GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void AnyOtherLineSilencesHumiliation()
    {
        GameEventMsg[] batch = AnnouncementDirector.Order(
            [Event(GameEventKind.HUMILIATION), Event(GameEventKind.FIRST_BLOOD)]);

        Assert.Equal([GameAnnouncer.Cue.FIRST_BLOOD], GameAnnouncer.Plan(batch, KILLER));
    }

    [Fact]
    public void MultiKillSwallowsTheStreakLine()
    {
        GameEventMsg[] batch = AnnouncementDirector.Order(
            [Event(GameEventKind.KILL_STREAK, magnitude: 5),
             Event(GameEventKind.MULTI_KILL, magnitude: 2)]);

        Assert.Equal([GameAnnouncer.Cue.DOUBLE_KILL], GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void HolyShitChainsIntoTheNextLine()
    {
        GameEventMsg[] batch = AnnouncementDirector.Order(
            [Event(GameEventKind.MULTI_KILL, magnitude: 3),
             Event(GameEventKind.HOLY_SHIT, magnitude: 3)]);

        Assert.Equal([GameAnnouncer.Cue.HOLY_SHIT, GameAnnouncer.Cue.TRIPLE_KILL],
            GameAnnouncer.Plan(batch, STRANGER));
    }

    [Fact]
    public void StreakAloneStillGetsItsLine()
    {
        Assert.Equal([GameAnnouncer.Cue.KILL_STREAK], GameAnnouncer.Plan(
            [Event(GameEventKind.KILL_STREAK, magnitude: 10)], STRANGER));
    }

    [Fact]
    public void BigMultiKillsShareTheGenericLine()
    {
        Assert.Equal([GameAnnouncer.Cue.MULTI_KILL], GameAnnouncer.Plan(
            [Event(GameEventKind.MULTI_KILL, magnitude: 5)], STRANGER));
    }

    [Fact]
    public void DirectorOrdersTheWholeVocabulary()
    {
        GameEventMsg[] ordered = AnnouncementDirector.Order(
            [Event(GameEventKind.HUMILIATION), Event(GameEventKind.KILL_STREAK),
             Event(GameEventKind.MULTI_KILL), Event(GameEventKind.SHUTDOWN),
             Event(GameEventKind.FIRST_BLOOD), Event(GameEventKind.HOLY_SHIT)]);

        Assert.Equal([GameEventKind.HOLY_SHIT, GameEventKind.FIRST_BLOOD,
            GameEventKind.SHUTDOWN, GameEventKind.MULTI_KILL,
            GameEventKind.KILL_STREAK, GameEventKind.HUMILIATION],
            ordered.Select(e => e.Kind));
    }

    private static GameEventMsg Event(GameEventKind kind, byte magnitude = 0) =>
        new(kind, KILLER, VICTIM, magnitude);
}
