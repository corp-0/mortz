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

    private static Team? TeamOf(long peerId) => peerId == KILLER ? Team.BLUE : Team.RED;

    [Fact]
    public void DescribeResolvesBothCombatants()
    {
        Announcement[] batch = AnnouncementDirector.Describe(
            [new GameEventMsg(GameEventKind.HUMILIATION, KILLER, VICTIM, 0)],
            NameOf, TeamOf);

        Announcement a = Assert.Single(batch);
        Assert.Equal(new Combatant(KILLER, "p1", Team.BLUE), a.Actor);
        Assert.Equal(new Combatant(VICTIM, "p2", Team.RED), a.Victim);
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
    public void SpecialEventsReplaceRegularKillsForTheSameActor()
    {
        Announcement[] resolved = AnnouncementDirector.Describe(
            [new GameEventMsg(GameEventKind.REGULAR_KILL, KILLER, VICTIM, 0),
             new GameEventMsg(GameEventKind.KILL_STREAK, KILLER, 0, 5),
             new GameEventMsg(GameEventKind.REGULAR_KILL, VICTIM, KILLER, 0),
             new GameEventMsg(GameEventKind.TEAM_KILL, KILLER, VICTIM, 0)],
            NameOf, TeamOf);

        Assert.Equal(
            [GameEventKind.KILL_STREAK, GameEventKind.REGULAR_KILL, GameEventKind.TEAM_KILL],
            resolved.Select(a => a.Kind));
        Assert.Equal(VICTIM, resolved[1].Actor.Id);
    }

    [Fact]
    public void MatchPointNamesThePlayerLeaderAndWearsTheirTeam()
    {
        MatchPointState state = AnnouncementDirector.Describe(
            new MatchPoint(3, new PlayerVictor((int)KILLER)), NameOf, TeamOf);

        Assert.Equal(3, state.Remaining);
        Assert.Equal(new MatchPointLeader("p1", Team.BLUE), state.Leader);
    }

    [Fact]
    public void MatchPointNamesTheTeamLeaderAndWearsItself()
    {
        MatchPointState state = AnnouncementDirector.Describe(
            new MatchPoint(1, new TeamVictor(Team.RED)), NameOf, TeamOf);

        Assert.Equal(new MatchPointLeader("Team Red", Team.RED), state.Leader);
    }

    [Fact]
    public void MatchPointCanHoldWithNobodyOnTheBoard()
    {
        // A kill target of 1: match point holds before anyone has scored.
        MatchPointState state = AnnouncementDirector.Describe(
            new MatchPoint(1, null), NameOf, TeamOf);

        Assert.Equal(1, state.Remaining);
        Assert.Null(state.Leader);
    }

    private static GameEventMsg Msg(GameEventKind kind) =>
        new(kind, KILLER, VICTIM, 0);
}
