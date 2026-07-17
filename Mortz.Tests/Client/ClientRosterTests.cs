using Mortz.Client.Roster;
using Mortz.Core.Net.Messages;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class ClientRosterTests : NodeServiceTest
{
    [Fact]
    public void LobbyStreamFeedsTheRoster()
    {
        ClientRoster roster = Host(new ClientRoster());

        new LobbyStateMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [], []).Broadcast();

        Assert.Equal("Alice", roster.NameOf(1));
        Assert.Equal("Bob", roster.NameOf(2));
    }

    [Fact]
    public void MatchStreamReplacesTheWholeRoster()
    {
        ClientRoster roster = Host(new ClientRoster());
        new LobbyStateMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [], []).Broadcast();

        new RosterMsg([2], ["Bobby"], [0], [0], [0]).Broadcast();

        Assert.Equal("Bobby", roster.NameOf(2));
        Assert.Equal("Player 1", roster.NameOf(1));
    }

    [Fact]
    public void UnknownPeersFallBackToPeerId()
    {
        ClientRoster roster = Host(new ClientRoster());
        Assert.Equal("Player 42", roster.NameOf(42));
    }
}
