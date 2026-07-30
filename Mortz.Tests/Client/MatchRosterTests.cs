using Mortz.Client.Roster;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class MatchRosterTests : NodeServiceTest
{
    [Fact]
    public void MatchStreamReplacesTheWholeRoster()
    {
        MatchRoster roster = Host(new MatchRoster());
        new RosterMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [1, 2]).Broadcast();

        new RosterMsg([2], ["Bobby"], [3], [2], [1]).Broadcast();

        Assert.Equal("Bobby", roster.NameOf(2));
        Assert.Equal("<unknown 1>", roster.NameOf(1));
        Assert.Equal(3, roster.SkinOf(2));
        Assert.Equal(Team.RED, roster.TeamOf(2));
        Assert.Equal(2, roster.Table.PeerInSlot(new NetSlot(1)));
    }

    [Fact]
    public void TheLobbyStreamDoesNotFeedTheRoster()
    {
        MatchRoster roster = Host(new MatchRoster());

        new LobbyStateMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 1], [], []).Broadcast();

        Assert.Equal("<unknown 1>", roster.NameOf(1));
        Assert.Null(roster.TeamOf(2));
        Assert.Null(roster.Table.PeerInSlot(new NetSlot(1)));
    }

    // The marker is deliberately unlike the server's "Player 42" fallback for a
    // blank name, so a nameplate showing it means the roster has not landed.
    [Fact]
    public void AnUnknownPeerGetsAMarkerNoRealNameCanMatch()
    {
        MatchRoster roster = Host(new MatchRoster());
        Assert.Equal("<unknown 42>", roster.NameOf(42));
        Assert.NotEqual("Player 42", roster.NameOf(42));
        Assert.Equal(0, roster.SkinOf(42));
        Assert.Null(roster.TeamOf(42));
    }

    [Fact]
    public void ChangedFiresOncePerSnapshot()
    {
        MatchRoster roster = Host(new MatchRoster());
        int changed = 0;
        roster.Changed += () => changed++;

        new RosterMsg([1], ["Alice"], [0], [0], [1]).Broadcast();
        Assert.Equal(1, changed);

        new RosterMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [1, 2]).Broadcast();
        Assert.Equal(2, changed);
    }

    [Fact]
    public void AMismatchedRosterLeavesThePreviousTableIntact()
    {
        MatchRoster roster = Host(new MatchRoster());
        new RosterMsg([1], ["Alice"], [0], [0], [1]).Broadcast();
        int changed = 0;
        roster.Changed += () => changed++;

        new RosterMsg([1, 2], ["Alice"], [0, 0], [0, 0], [1, 2]).Broadcast();

        Assert.Equal("Alice", roster.NameOf(1));
        Assert.Equal("<unknown 2>", roster.NameOf(2));
        Assert.Equal(0, changed);
    }
}
