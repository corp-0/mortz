using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Malformed roster payloads at the decode boundary. The row
/// constructors make bad rows unrepresentable in code, so rejection can only
/// be provoked with hand-written bytes; round trips live in NetMessageTests.</summary>
public class RosterWireTests
{
    [Fact]
    public void AnInvalidPeerIdDropsTheWholeMatchRoster() =>
        Assert.False(DispatchRoster(RosterPayload(peerId: 0, slot: 1)));

    [Fact]
    public void AnUnholdableSlotDropsTheWholeMatchRoster()
    {
        Assert.False(DispatchRoster(RosterPayload(peerId: 1, slot: 0)));
        Assert.False(DispatchRoster(
            RosterPayload(peerId: 1, slot: NetConfig.MAX_PLAYERS + 1)));
    }

    [Fact]
    public void AnInvalidPeerIdDropsTheWholeLobbyRoster()
    {
        NetRouter router = new();
        router.Add(new ClientProbe<LobbyStateMsg>());

        Assert.False(router.Dispatch(LobbyStateMsg.MsgId, LobbyPayload(peerId: 0)));
    }

    [Fact]
    public void AnUnknownTeamByteDecodesToNoTeam()
    {
        NetRouter router = new();
        ClientProbe<LobbyStateMsg> probe = new();
        router.Add(probe);

        Assert.True(router.Dispatch(LobbyStateMsg.MsgId, LobbyPayload(peerId: 1, team: 9)));

        LobbyMember member = Assert.Single(Assert.Single(probe.Messages).Members);
        Assert.Null(member.Team);
    }

    private static bool DispatchRoster(byte[] payload)
    {
        NetRouter router = new();
        router.Add(new ClientProbe<RosterMsg>());
        return router.Dispatch(RosterMsg.MsgId, payload);
    }

    /// <summary>One roster row, wire order: count, peer, name, skin, team, slot.</summary>
    private static byte[] RosterPayload(int peerId, byte slot)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write(1);
        w.Write(peerId);
        w.Write("A");
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write(slot);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>One lobby member and no offers, wire order: count, peer, name,
    /// ready, team, then the offer count.</summary>
    private static byte[] LobbyPayload(int peerId, byte team = 0)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write(1);
        w.Write(peerId);
        w.Write("A");
        w.Write(false);
        w.Write(team);
        w.Write(0);
        w.Flush();
        return ms.ToArray();
    }
}
