using Mortz.Client.Score;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>Drives the score tables through the real wire path (loopback
/// NetTransport), so serialization and dispatch are covered too. Swaps the
/// NetTransport.Send static, hence the shared NetTransport collection.</summary>
[Collection("NetTransport")]
public class MatchScoreSessionTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public MatchScoreSessionTests() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, 1, payload, isServer: false));

    public void Dispose() => NetTransport.Send = _original;

    [Fact]
    public void SyncSeedReplacesEverything()
    {
        using MatchScoreSession session = new();
        session.Subscribe();
        new ScoreSyncMsg([7], [9], [9], 9, 9).SendTo(1);

        new ScoreSyncMsg([7, 8], [3, 1], [0, 2], 4, 2).SendTo(1);

        Assert.Equal(3, session.Kills(7));
        Assert.Equal(1, session.Kills(8));
        Assert.Equal(0, session.Deaths(7));
        Assert.Equal(2, session.Deaths(8));
        Assert.Equal(4, session.TeamKills(1));
        Assert.Equal(2, session.TeamKills(2));
    }

    [Fact]
    public void EliminationsPatchTheAffectedRows()
    {
        using MatchScoreSession session = new();
        session.Subscribe();
        new ScoreSyncMsg([7, 8], [3, 1], [0, 2], 4, 2).SendTo(1);

        new EliminationMsg(7, 8, EliminationFlags.NONE, 4, 3, 5, 2).Broadcast();

        Assert.Equal(4, session.Kills(7));
        Assert.Equal(3, session.Deaths(8));
        Assert.Equal(1, session.Kills(8));
        Assert.Equal(5, session.TeamKills(1));
        Assert.Equal(2, session.TeamKills(2));
    }

    [Fact]
    public void SuicidePenaltyLandsOnTheVictim()
    {
        using MatchScoreSession session = new();
        session.Subscribe();
        new ScoreSyncMsg([8], [2], [0], 2, 0).SendTo(1);

        new EliminationMsg(0, 8, EliminationFlags.SUICIDE | EliminationFlags.FALL,
            1, 1, 1, 0).Broadcast();

        Assert.Equal(1, session.Kills(8));
        Assert.Equal(1, session.Deaths(8));
        Assert.Equal(1, session.TeamKills(1));
    }

    [Fact]
    public void ClearForgetsEverythingAndNotifies()
    {
        using MatchScoreSession session = new();
        session.Subscribe();
        new ScoreSyncMsg([7], [3], [1], 3, 0).SendTo(1);
        int changes = 0;
        session.Changed += () => changes++;

        session.Clear();

        Assert.Equal(0, session.Kills(7));
        Assert.Equal(0, session.Deaths(7));
        Assert.Equal(0, session.TeamKills(1));
        Assert.Equal(1, changes);
    }
}
