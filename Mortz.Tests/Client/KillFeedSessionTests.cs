using Mortz.Client.Feed;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>Drives the feed through the real wire path (loopback
/// NetTransport), so serialization and dispatch are covered too. Swaps the
/// NetTransport.Send static, hence the shared NetTransport collection.</summary>
[Collection("NetTransport")]
public class KillFeedSessionTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public KillFeedSessionTests() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, 1, payload, isServer: false));

    public void Dispose() => NetTransport.Send = _original;

    private static string Name(long id) => id switch
    {
        1 => "Alice",
        2 => "Bob",
        _ => $"Player {id}",
    };

    [Fact]
    public void EliminationBecomesAFeedLine()
    {
        using KillFeedSession session = new(Name);
        session.Subscribe();
        List<string> lines = [];
        session.LineAdded += lines.Add;

        new EliminationMsg(1, 2, EliminationFlags.NONE, 1, 1, 0, 0).Broadcast();

        Assert.Equal(["Alice killed Bob"], lines);
    }

    [Fact]
    public void MatchEndNamesTheWinnerOrTeam()
    {
        using KillFeedSession session = new(Name);
        session.Subscribe();
        List<string> lines = [];
        session.LineAdded += lines.Add;

        new MatchEndMsg(false, 1).Broadcast();
        new MatchEndMsg(true, 2).Broadcast();

        Assert.Equal(["Alice wins!", "Team 2 wins!"], lines);
    }

    [Fact]
    public void DisposeStopsTheFeed()
    {
        KillFeedSession session = new(Name);
        session.Subscribe();
        List<string> lines = [];
        session.LineAdded += lines.Add;
        session.Dispose();

        new EliminationMsg(1, 2, EliminationFlags.NONE, 1, 1, 0, 0).Broadcast();

        Assert.Empty(lines);
    }
}
