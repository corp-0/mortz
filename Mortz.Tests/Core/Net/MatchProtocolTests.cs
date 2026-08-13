using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// NetMessageTests.</summary>
[Collection("NetTransport")]
public class MatchProtocolTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static NetRouter UseLoopback()
    {
        NetRouter router = new();
        NetTransport.Send = (id, payload, _, _) => Assert.True(router.Dispatch(id, payload));
        return router;
    }

    private static Victor? BroadcastEnd(Victor winner) =>
        SendRawEnd(MatchProtocol.Encode(winner));

    private static MatchPoint? BroadcastMatchPoint(MatchPoint? state) =>
        SendRawMatchPoint(MatchProtocol.Encode(state));

    /// <summary>Malformed messages cannot be built from a Victor, so raw
    /// messages send by hand.</summary>
    private static Victor? SendRawEnd(MatchEndMsg message)
    {
        NetRouter router = UseLoopback();
        ClientProbe<MatchEndMsg> probe = new();
        router.Add(probe);
        message.Broadcast();
        MatchEndMsg received = Assert.Single(probe.Messages);
        return MatchProtocol.TryDecode(received, out Victor? winner) ? winner : null;
    }

    private static MatchPoint? SendRawMatchPoint(MatchPointMsg message)
    {
        NetRouter router = UseLoopback();
        ClientProbe<MatchPointMsg> probe = new();
        router.Add(probe);
        message.Broadcast();
        // The transition must reach consumers even when it carries no state.
        return MatchProtocol.Decode(Assert.Single(probe.Messages));
    }

    [Fact]
    public void APlayerWinnerRoundTrips() =>
        Assert.Equal(new Victor.Player(778900112), BroadcastEnd(new Victor.Player(778900112)));

    [Fact]
    public void ATeamWinnerRoundTrips()
    {
        Assert.Equal(new Victor.Team(Team.BLUE), BroadcastEnd(new Victor.Team(Team.BLUE)));
        Assert.Equal(new Victor.Team(Team.RED), BroadcastEnd(new Victor.Team(Team.RED)));
    }

    [Fact]
    public void AWinnerNobodyCanNameIsDropped()
    {
        Assert.Null(SendRawEnd(new MatchEndMsg(ByTeam: false, WinnerId: 0)));
        Assert.Null(SendRawEnd(new MatchEndMsg(ByTeam: true, WinnerId: 9)));
    }

    [Fact]
    public void MatchPointWithAPlayerLeaderRoundTrips() =>
        Assert.Equal(new MatchPoint(3, new Victor.Player(7)),
            BroadcastMatchPoint(new MatchPoint(3, new Victor.Player(7))));

    [Fact]
    public void MatchPointWithATeamLeaderRoundTrips() =>
        Assert.Equal(new MatchPoint(1, new Victor.Team(Team.RED)),
            BroadcastMatchPoint(new MatchPoint(1, new Victor.Team(Team.RED))));

    [Fact]
    public void MatchPointWithoutALeaderRoundTrips() =>
        Assert.Equal(new MatchPoint(2, null), BroadcastMatchPoint(new MatchPoint(2, null)));

    [Fact]
    public void ALapseArrivesAsNoState() => Assert.Null(BroadcastMatchPoint(null));

    [Fact]
    public void ALeaderNobodyCanNameKeepsTheStateAndLosesTheName() =>
        Assert.Equal(new MatchPoint(4, null),
            SendRawMatchPoint(new MatchPointMsg(true, 4, 9,
                LeaderIsTeam: true)));

    /// <summary>A zero would trip MatchPoint's own check, so the decoder
    /// clamps it.</summary>
    [Fact]
    public void AnImpossibleRemainingIsSanitizedNotThrown() =>
        Assert.Equal(new MatchPoint(1, null),
            SendRawMatchPoint(new MatchPointMsg(true, 0)));
}
