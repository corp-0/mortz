using Mortz.Server.Phases;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server;

public class RosterTests
{
    private sealed class Tally
    {
        public int Count;
    }

    private readonly ServerStateKeys _keys = new(generation: 1);

    [Fact]
    public void ASelectedSkinBecomesPartOfThePlayerIdentity()
    {
        Roster roster = new(_keys);

        Player player = roster.Join(1, "hello", requestedSkin: 24);

        Assert.Equal(24, player.Skin);
        Assert.Throws<ArgumentOutOfRangeException>(() => roster.Join(2, "bad", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => roster.Join(3, "bad", 25));
    }

    [Fact]
    public void ABlankNameFallsBackToThePeerId()
    {
        Roster roster = new(_keys);

        Assert.Equal("Player 7", roster.Join(7, "   ").Name);
        Assert.Equal("Player 8", roster.Join(8, "").Name);
    }

    [Fact]
    public void ANameOfNothingButControlRunesFallsBackToo()
    {
        Roster roster = new(_keys);

        Assert.Equal("Player 3", roster.Join(3, "\r\n‮").Name);
    }

    [Fact]
    public void ANameIsSanitizedOnce()
    {
        Roster roster = new(_keys);

        Assert.Equal("hello", roster.Join(1, "  hello‮  ").Name);
    }

    [Fact]
    public void IterationIsAscendingPeerIdRegardlessOfJoinOrder()
    {
        Roster roster = new(_keys);
        roster.Join(9, "nine");
        roster.Join(2, "two");
        roster.Join(5, "five");

        Assert.Equal([2, 5, 9], roster.Select(player => player.PeerId).ToArray());
    }

    [Fact]
    public void FindReturnsTheSamePlayerAndNullForAStranger()
    {
        Roster roster = new(_keys);
        Player joined = roster.Join(4, "four");

        Assert.Same(joined, roster.Find(4));
        Assert.Null(roster.Find(5));
    }

    [Fact]
    public void LeavingRemovesThePlayerAndReturnsThemOnce()
    {
        Roster roster = new(_keys);
        Player joined = roster.Join(4, "four");

        Assert.Same(joined, roster.Leave(4));
        Assert.Null(roster.Leave(4));
        Assert.Null(roster.Find(4));
        Assert.Empty(roster);
    }

    [Fact]
    public void RejoiningMintsAFreshPlayerWithFreshState()
    {
        ServerStateKey<Tally> key = _keys.Claim<Tally>();
        Roster roster = new(_keys);
        Player before = roster.Join(4, "four");
        before.State(key).Count = 5;

        roster.Leave(4);
        before.Close(ServerPhaseKind.LOBBY);
        Player after = roster.Join(4, "four");

        Assert.NotSame(before, after);
        Assert.Equal(0, after.State(key).Count);
    }

    [Fact]
    public void JoiningTwiceOnOnePeerReplacesTheEntry()
    {
        Roster roster = new(_keys);
        roster.Join(4, "four");
        Player second = roster.Join(4, "again");

        Assert.Same(second, Assert.Single(roster));
    }

    [Fact]
    public void EveryPlayerIsSizedForEveryKeyClaimedBeforeTheyJoined()
    {
        ServerStateKey<Tally> first = _keys.Claim<Tally>();
        ServerStateKey<Tally> second = _keys.Claim<Tally>();
        Roster roster = new(_keys);

        Player player = roster.Join(1, "one");

        Assert.Equal(0, player.State(first).Count);
        Assert.Equal(0, player.State(second).Count);
    }
}
