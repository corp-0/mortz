using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// MatchProtocolTests.</summary>
[Collection("NetTransport")]
public class RosterProtocolTests : IDisposable
{
    private const long SENDER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static void UseLoopback() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, SENDER, payload, isServer: false));

    private static RosterSnapshot? Broadcast(params RosterEntry[] entries)
    {
        UseLoopback();
        RosterSnapshot? received = null;
        Action<RosterSnapshot> handler = snapshot => received = snapshot;
        RosterProtocol.MatchRosterReceived += handler;
        try
        {
            RosterProtocol.BroadcastMatchRoster(new RosterSnapshot(entries));
        }
        finally
        {
            RosterProtocol.MatchRosterReceived -= handler;
        }
        return received;
    }

    private static LobbyRoster? Broadcast(LobbyRoster roster)
    {
        UseLoopback();
        LobbyRoster? received = null;
        Action<LobbyRoster> handler = decoded => received = decoded;
        RosterProtocol.LobbyRosterReceived += handler;
        try
        {
            RosterProtocol.BroadcastLobbyRoster(roster);
        }
        finally
        {
            RosterProtocol.LobbyRosterReceived -= handler;
        }
        return received;
    }

    /// <summary>Mismatched arrays cannot be built from a row list, so send them
    /// by hand.</summary>
    private static RosterSnapshot? SendRaw(RosterMsg message)
    {
        UseLoopback();
        RosterSnapshot? received = null;
        Action<RosterSnapshot> handler = snapshot => received = snapshot;
        RosterProtocol.MatchRosterReceived += handler;
        try
        {
            message.Broadcast();
        }
        finally
        {
            RosterProtocol.MatchRosterReceived -= handler;
        }
        return received;
    }

    private static LobbyRoster? SendRaw(LobbyStateMsg message)
    {
        UseLoopback();
        LobbyRoster? received = null;
        Action<LobbyRoster> handler = decoded => received = decoded;
        RosterProtocol.LobbyRosterReceived += handler;
        try
        {
            message.Broadcast();
        }
        finally
        {
            RosterProtocol.LobbyRosterReceived -= handler;
        }
        return received;
    }

    [Fact]
    public void AMatchRosterRoundTrips()
    {
        RosterEntry[] entries =
        [
            new RosterEntry(11, "Alice", 2, Team.BLUE, new NetSlot(1)),
            new RosterEntry(22, "Bob", 5, Team.RED, new NetSlot(2)),
            new RosterEntry(33, "Cara", 0, null, new NetSlot(3)),
        ];

        Assert.Equal(entries, Broadcast(entries)?.Entries);
    }

    [Fact]
    public void AnEmptyMatchRosterStillArrives() => Assert.Empty(Broadcast()!.Entries);

    [Fact]
    public void ALobbyRosterRoundTrips()
    {
        LobbyRoster roster = new(
            [
                new LobbyMember(11, "Alice", true, Team.BLUE),
                new LobbyMember(22, "Bob", false, null),
            ],
            [new SwapOffer(11, 22)]);

        LobbyRoster? received = Broadcast(roster);

        Assert.Equal(roster.Members, received?.Members);
        Assert.Equal(roster.Offers, received?.Offers);
    }

    [Fact]
    public void EveryShortArrayDropsTheWholeMatchRoster()
    {
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A"], [0, 0], [0, 0], [1, 2])));
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A", "B"], [0], [0, 0], [1, 2])));
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A", "B"], [0, 0], [0], [1, 2])));
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A", "B"], [0, 0], [0, 0], [1])));
        Assert.Null(SendRaw(new RosterMsg([1], ["A", "B"], [0, 0], [0, 0], [1, 2])));
    }

    [Fact]
    public void AnInvalidPeerIdDropsTheWholeMatchRoster() =>
        Assert.Null(SendRaw(new RosterMsg([1, 0], ["A", "B"], [0, 0], [0, 0], [1, 2])));

    [Fact]
    public void AnUnholdableSlotDropsTheWholeMatchRoster()
    {
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A", "B"], [0, 0], [0, 0], [1, 0])));
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A", "B"], [0, 0], [0, 0],
            [1, NetConfig.MAX_PLAYERS + 1])));
    }

    [Fact]
    public void ADuplicateSlotDropsTheWholeMatchRoster() =>
        Assert.Null(SendRaw(new RosterMsg([1, 2], ["A", "B"], [0, 0], [0, 0], [1, 1])));

    [Fact]
    public void ADuplicatePeerDropsTheWholeMatchRoster() =>
        Assert.Null(SendRaw(new RosterMsg([1, 1], ["A", "B"], [0, 0], [0, 0], [1, 2])));

    [Fact]
    public void EveryShortArrayDropsTheWholeLobbyRoster()
    {
        Assert.Null(SendRaw(new LobbyStateMsg([1, 2], ["A"], [0, 0], [0, 0], [], [])));
        Assert.Null(SendRaw(new LobbyStateMsg([1, 2], ["A", "B"], [0], [0, 0], [], [])));
        Assert.Null(SendRaw(new LobbyStateMsg([1, 2], ["A", "B"], [0, 0], [0], [], [])));
        Assert.Null(SendRaw(new LobbyStateMsg([1, 2], ["A", "B"], [0, 0], [0, 0], [1], [])));
    }

    [Fact]
    public void AnInvalidPeerIdDropsTheWholeLobbyRoster() =>
        Assert.Null(SendRaw(new LobbyStateMsg([0, 2], ["A", "B"], [0, 0], [0, 0], [], [])));

    [Fact]
    public void TeamBytesDecodeToTeams()
    {
        LobbyRoster? received = SendRaw(
            new LobbyStateMsg([1, 2, 3], ["A", "B", "C"], [0, 0, 0], [0, 1, 2], [], []));

        Assert.Equal([null, Team.BLUE, Team.RED],
            received!.Members.Select(member => member.Team));
    }
}
