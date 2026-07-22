using Chickensoft.AutoInject;
using Mortz.Client.Feed;
using Mortz.Client.Roster;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client.Feed;

[Collection(nameof(MortzGodotCollection))]
public class KillFeedTests : NodeServiceTest
{
    [Fact]
    public void FormatsEliminationsAndMatchEndWithResolvedNames()
    {
        ClientRoster roster = Host(new ClientRoster());
        KillFeed feed = new();
        feed.FakeDependency(roster);
        Host(feed);
        List<string> lines = [];
        feed.LineAdded += lines.Add;

        new RosterMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [0, 1]).Broadcast();
        new EliminationMsg(1, 2, EliminationFlags.NONE, 1, 1, 0, 0).Broadcast();
        new MatchEndMsg(false, 1).Broadcast();
        new MatchEndMsg(true, 2).Broadcast();

        Assert.Equal(["Alice killed Bob", "Alice wins!", "Team 2 wins!"], lines);
    }
}
