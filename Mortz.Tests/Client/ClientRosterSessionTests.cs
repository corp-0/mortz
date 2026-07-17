using Mortz.Client.Roster;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>Drives the roster through the real wire path (loopback
/// NetTransport), so serialization and dispatch are covered too. Swaps the
/// NetTransport.Send static, hence the shared NetTransport collection.</summary>
[Collection("NetTransport")]
public class ClientRosterSessionTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public ClientRosterSessionTests() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, 1, payload, isServer: false));

    public void Dispose() => NetTransport.Send = _original;

    [Fact]
    public void LobbyStreamFeedsTheRoster()
    {
        using ClientRosterSession session = new();
        session.Subscribe();

        new LobbyStateMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [], []).Broadcast();

        Assert.Equal("Alice", session.NameOf(1));
        Assert.Equal("Bob", session.NameOf(2));
    }

    [Fact]
    public void MatchStreamReplacesTheWholeRoster()
    {
        using ClientRosterSession session = new();
        session.Subscribe();
        new LobbyStateMsg([1, 2], ["Alice", "Bob"], [0, 0], [0, 0], [], []).Broadcast();

        new RosterMsg([2], ["Bobby"], [0], [0], [0]).Broadcast();

        Assert.Equal("Bobby", session.NameOf(2));
        Assert.Equal("Player 1", session.NameOf(1));
    }

    [Fact]
    public void UnknownPeersFallBackToPeerId()
    {
        using ClientRosterSession session = new();
        Assert.Equal("Player 42", session.NameOf(42));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        using ClientRosterSession session = new();
        session.Subscribe();
        new RosterMsg([1], ["Alice"], [0], [0], [0]).Broadcast();

        session.Clear();

        Assert.Equal("Player 1", session.NameOf(1));
    }
}
