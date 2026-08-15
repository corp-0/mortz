using Mortz.Core.Match.Participation;
using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Server;
using Xunit;

namespace Mortz.Tests.Server;

public sealed class ReadyLinkTests
{
    [Fact]
    public void QueuesInOrderUntilCurrentGenerationIsReady()
    {
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(7, generation: 3, nowMs: 10);

        link.Send(7, Message("one"));
        link.Send(7, Message("two"));

        Assert.Empty(wire.Messages);
        Assert.False(link.Ready(7, generation: 2));
        Assert.True(link.Ready(7, generation: 3));
        Assert.Equal(["one", "two"], wire.Messages.Select(sent => ((ChatMsg)sent.Message).Text));
    }

    [Fact]
    public void BootstrapPassesWhileScreenTrafficWaits()
    {
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(7, generation: 3, nowMs: 10);

        link.Send(7, Message("waiting"));
        link.Send(7, new LobbyLoadMsg(3));

        Assert.IsType<LobbyLoadMsg>(Assert.Single(wire.Messages).Message);
    }

    [Fact]
    public void MatchBootstrapPassesWhileScreenTrafficWaits()
    {
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(7, generation: 3, nowMs: 10);

        link.Send(7, Message("waiting"));
        link.Send(7, new MatchLoadMsg(
            "arena", "hash", [], 0, 1, 0, 1,
            MatchSeat.PLAYER, MatchActivity.ACTIVE, SpectateReason.NONE, -1,
            [1], -1, 3));

        Assert.IsType<MatchLoadMsg>(Assert.Single(wire.Messages).Message);
    }

    [Fact]
    public void DuplicateReadyAcknowledgementDoesNothing()
    {
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(7, generation: 3, nowMs: 10);
        link.Send(7, Message("once"));

        Assert.True(link.Ready(7, generation: 3));
        Assert.False(link.Ready(7, generation: 3));

        Assert.Equal(["once"], wire.Messages.Select(sent => ((ChatMsg)sent.Message).Text));
    }

    [Fact]
    public void QueueOverflowDisconnectsAndDropsPendingTrafficOnce()
    {
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(7, generation: 3, nowMs: 10);

        for (int i = 0; i <= NetConfig.MAX_LOADING_MESSAGES; i++)
        {
            link.Send(7, Message($"message {i}"));
        }
        link.Send(7, Message("after overflow"));

        Assert.Equal([7], wire.Disconnected);
        Assert.Empty(wire.Messages);
        Assert.False(link.Ready(7, generation: 3));
    }

    [Fact]
    public void DisconnectsAClientThatNeverBecomesReady()
    {
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(7, generation: 3, nowMs: 10);

        link.DisconnectExpired(10 + NetConfig.PHASE_READY_TIMEOUT_MS);

        Assert.Equal([7], wire.Disconnected);
    }

    private static ChatMsg Message(string text) =>
        new(ChatMsgKind.SYSTEM, 0, "", text, ChatTextFormat.PLAIN);
}
