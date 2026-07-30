using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// MatchProtocolTests.</summary>
[Collection("NetTransport")]
public class ScoreProtocolTests : IDisposable
{
    private const long PEER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static void UseLoopback() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, PEER, payload, isServer: false));

    private static ScoreSync? Send(ScoreSync sync)
    {
        UseLoopback();
        ScoreSync? received = null;
        Action<ScoreSync> handler = decoded => received = decoded;
        ScoreProtocol.Received += handler;
        try
        {
            ScoreProtocol.SendTo(PEER, sync);
        }
        finally
        {
            ScoreProtocol.Received -= handler;
        }
        return received;
    }

    private static ScoreSync? SendRaw(ScoreSyncMsg message)
    {
        UseLoopback();
        ScoreSync? received = null;
        Action<ScoreSync> handler = decoded => received = decoded;
        ScoreProtocol.Received += handler;
        try
        {
            message.SendTo(PEER);
        }
        finally
        {
            ScoreProtocol.Received -= handler;
        }
        return received;
    }

    [Fact]
    public void TheSeedRoundTrips()
    {
        ScoreSync sent = new(
            [new ScoreRow(11, 4, 1), new ScoreRow(22, 0, 7)],
            new TeamKills(4, 9));

        ScoreSync? received = Send(sent);

        Assert.Equal(sent.Rows, received?.Rows);
        Assert.Equal(sent.TeamKills, received?.TeamKills);
    }

    [Fact]
    public void AShortColumnDropsTheWholeSeed()
    {
        Assert.Null(SendRaw(new ScoreSyncMsg([1, 2], [1], [1, 2], 0, 0)));
        Assert.Null(SendRaw(new ScoreSyncMsg([1, 2], [1, 2], [1], 0, 0)));
        Assert.Null(SendRaw(new ScoreSyncMsg([1], [1, 2], [1, 2], 0, 0)));
    }
}
