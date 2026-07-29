using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// NetMessageTests.</summary>
[Collection("NetTransport")]
public class MatchProtocolTests : IDisposable
{
    private const long SENDER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static void UseLoopback() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, SENDER, payload, isServer: false));

    private static Victor? BroadcastEnd(Victor winner)
    {
        UseLoopback();
        Victor? received = null;
        Action<Victor> handler = victor => received = victor;
        MatchProtocol.MatchEnded += handler;
        try
        {
            MatchProtocol.BroadcastMatchEnd(winner);
        }
        finally
        {
            MatchProtocol.MatchEnded -= handler;
        }
        return received;
    }

    private static MatchPoint? BroadcastMatchPoint(MatchPoint? state)
    {
        UseLoopback();
        MatchPoint? received = null;
        bool raised = false;
        Action<MatchPoint?> handler = point =>
        {
            received = point;
            raised = true;
        };
        MatchProtocol.MatchPointChanged += handler;
        try
        {
            MatchProtocol.BroadcastMatchPoint(WinCondition.KILLS, state);
        }
        finally
        {
            MatchProtocol.MatchPointChanged -= handler;
        }
        Assert.True(raised, "the transition must reach consumers");
        return received;
    }

    /// <summary>Malformed messages cannot be built from a Victor, so send them
    /// by hand.</summary>
    private static Victor? SendRawEnd(bool byTeam, long winnerId)
    {
        UseLoopback();
        Victor? received = null;
        Action<Victor> handler = victor => received = victor;
        MatchProtocol.MatchEnded += handler;
        try
        {
            new MatchEndMsg(byTeam, winnerId).Broadcast();
        }
        finally
        {
            MatchProtocol.MatchEnded -= handler;
        }
        return received;
    }

    [Fact]
    public void APlayerWinnerRoundTrips() =>
        Assert.Equal(new PlayerVictor(778900112), BroadcastEnd(new PlayerVictor(778900112)));

    [Fact]
    public void ATeamWinnerRoundTrips()
    {
        Assert.Equal(new TeamVictor(Team.BLUE), BroadcastEnd(new TeamVictor(Team.BLUE)));
        Assert.Equal(new TeamVictor(Team.RED), BroadcastEnd(new TeamVictor(Team.RED)));
    }

    [Fact]
    public void AWinnerNobodyCanNameIsDropped()
    {
        Assert.Null(SendRawEnd(byTeam: false, winnerId: 0));
        Assert.Null(SendRawEnd(byTeam: true, winnerId: 9));
    }

    [Fact]
    public void MatchPointWithAPlayerLeaderRoundTrips() =>
        Assert.Equal(new MatchPoint(3, new PlayerVictor(7)),
            BroadcastMatchPoint(new MatchPoint(3, new PlayerVictor(7))));

    [Fact]
    public void MatchPointWithATeamLeaderRoundTrips() =>
        Assert.Equal(new MatchPoint(1, new TeamVictor(Team.RED)),
            BroadcastMatchPoint(new MatchPoint(1, new TeamVictor(Team.RED))));

    [Fact]
    public void MatchPointWithoutALeaderRoundTrips() =>
        Assert.Equal(new MatchPoint(2, null), BroadcastMatchPoint(new MatchPoint(2, null)));

    [Fact]
    public void ALapseArrivesAsNoState() => Assert.Null(BroadcastMatchPoint(null));

    [Fact]
    public void ALeaderNobodyCanNameKeepsTheStateAndLosesTheName()
    {
        UseLoopback();
        MatchPoint? received = null;
        Action<MatchPoint?> handler = point => received = point;
        MatchProtocol.MatchPointChanged += handler;
        try
        {
            new MatchPointMsg(true, WinCondition.KILLS, 4, 9, LeaderIsTeam: true).Broadcast();
        }
        finally
        {
            MatchProtocol.MatchPointChanged -= handler;
        }
        Assert.Equal(new MatchPoint(4, null), received);
    }

    /// <summary>A zero would trip MatchPoint's own check, so the decoder
    /// clamps it.</summary>
    [Fact]
    public void AnImpossibleRemainingIsSanitizedNotThrown()
    {
        UseLoopback();
        MatchPoint? received = null;
        Action<MatchPoint?> handler = point => received = point;
        MatchProtocol.MatchPointChanged += handler;
        try
        {
            new MatchPointMsg(true, WinCondition.KILLS, 0).Broadcast();
        }
        finally
        {
            MatchProtocol.MatchPointChanged -= handler;
        }
        Assert.Equal(new MatchPoint(1, null), received);
    }
}
