using Mortz.Client.Announcements;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client.Announcements;

public class AnnouncementDirectorTests
{
    private const long KILLER = 7;
    private const long VICTIM = 8;

    private static string NameOf(long peerId) => peerId == KILLER ? "p1" : "p2";

    private static byte TeamOf(long peerId) => peerId == KILLER ? (byte)1 : (byte)2;

    [Fact]
    public void DescribeResolvesBothCombatants()
    {
        Announcement[] batch = AnnouncementDirector.Describe(
            [new GameEventMsg(GameEventKind.HUMILIATION, KILLER, VICTIM, 0)],
            NameOf, TeamOf);

        Announcement a = Assert.Single(batch);
        Assert.Equal(new Combatant(KILLER, "p1", 1), a.Actor);
        Assert.Equal(new Combatant(VICTIM, "p2", 2), a.Victim);
    }

    [Fact]
    public void DescribeCarriesTheSuicideCause()
    {
        Announcement[] batch = AnnouncementDirector.Describe(
            [new GameEventMsg(GameEventKind.SUICIDE, KILLER, 0, 1, (byte)SuicideCause.FALL)],
            NameOf, TeamOf);

        Assert.Equal(SuicideCause.FALL, Assert.Single(batch).Cause);
    }

    [Fact]
    public void DescribeOrdersTheWholeVocabulary()
    {
        // The multi kill is the victim's, so the holy-shit fold leaves it alone.
        Announcement[] ordered = AnnouncementDirector.Describe(
            [Msg(GameEventKind.HUMILIATION), Msg(GameEventKind.KILL_STREAK),
             new GameEventMsg(GameEventKind.MULTI_KILL, VICTIM, 0, 2),
             Msg(GameEventKind.SHUTDOWN), Msg(GameEventKind.TEAM_WIPE),
             Msg(GameEventKind.FIRST_BLOOD), Msg(GameEventKind.HOLY_SHIT)],
            NameOf, TeamOf);

        Assert.Equal([GameEventKind.HOLY_SHIT, GameEventKind.FIRST_BLOOD,
            GameEventKind.TEAM_WIPE, GameEventKind.SHUTDOWN, GameEventKind.MULTI_KILL,
            GameEventKind.KILL_STREAK, GameEventKind.HUMILIATION],
            ordered.Select(a => a.Kind));
    }

    [Fact]
    public void FoldingReplacesTheMultiKillWithTheComposite()
    {
        Announcement[] folded = AnnouncementDirector.Describe(
            [new GameEventMsg(GameEventKind.HOLY_SHIT, KILLER, 0, 3),
             new GameEventMsg(GameEventKind.MULTI_KILL, KILLER, 0, 5),
             new GameEventMsg(GameEventKind.MULTI_KILL, VICTIM, 0, 2)],
            NameOf, TeamOf);

        Assert.Equal([(GameEventKind.HOLY_SHIT, KILLER, (byte)5),
            (GameEventKind.MULTI_KILL, VICTIM, (byte)2)],
            folded.Select(a => (a.Kind, a.Actor.Id, a.Magnitude)));
    }

    [Fact]
    public void MatchPointNamesTheLeader()
    {
        Assert.Equal("p1", AnnouncementDirector.Describe(
            new MatchPointMsg(true, WinCondition.PLAYER_KILLS, 1, KILLER), NameOf).Leader);
        Assert.Equal("Team 2", AnnouncementDirector.Describe(
            new MatchPointMsg(true, WinCondition.TEAM_KILLS, 1, 2, LeaderIsTeam: true),
            NameOf).Leader);
        // A kill target of 1: match point holds with nobody on the board.
        Assert.Null(AnnouncementDirector.Describe(
            new MatchPointMsg(true, WinCondition.PLAYER_KILLS, 1, 0), NameOf).Leader);
    }

    private static GameEventMsg Msg(GameEventKind kind) =>
        new(kind, KILLER, VICTIM, 0);
}
