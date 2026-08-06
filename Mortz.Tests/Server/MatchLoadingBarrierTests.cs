using Mortz.Core.Input;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Sim;
using Mortz.Core.Sim;
using Mortz.Server.Diagnostics;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Tests.Server;

public sealed class MatchLoadingBarrierTests : IDisposable
{
    private readonly RecordingObserver _observer = new();
    private readonly TestServer _server;

    public MatchLoadingBarrierTests() => _server = new TestServer(observer: _observer);

    public void Dispose() => _server.Dispose();

    [Fact]
    public void MatchDoesNotAdvanceUntilTheWholeLobbyCohortIsReady()
    {
        BeginLoadingMatch();

        _server.AdvanceWithoutReady();
        Assert.Null(_observer.LastFrame);
        Assert.DoesNotContain(_server.Link.Messages, sent => sent.Message is MatchStartMsg);

        _server.Ready(7);
        _server.AdvanceWithoutReady();
        Assert.Null(_observer.LastFrame);

        _server.Ready(8);
        Assert.Contains(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
        Assert.Null(_observer.LastFrame);

        _server.AdvanceWithoutReady();
        Assert.NotNull(_observer.LastFrame);
    }

    [Fact]
    public void StaleReadyDoesNotReleaseTheBarrier()
    {
        BeginLoadingMatch();
        int generation = _server.Link.Last<WelcomeMsg>().Generation;

        _server.Receive(7, new PhaseReadyMsg(generation - 1));
        _server.Ready(8);

        Assert.DoesNotContain(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
        Assert.Null(_observer.LastFrame);
    }

    [Fact]
    public void DisconnectingAPendingPlayerReleasesTheRemainingCohort()
    {
        BeginLoadingMatch();
        _server.Ready(7);

        _server.Server.Disconnect(8);

        Assert.Contains(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
    }

    [Fact]
    public void AJoinDuringLoadingDoesNotExtendTheLobbyCohort()
    {
        BeginLoadingMatch();
        _server.Server.Connect(9, "jip");

        _server.Ready(7);
        _server.Ready(8);

        Assert.Contains(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
    }

    [Fact]
    public void LosingEveryoneReturnsToTheLobby()
    {
        BeginLoadingMatch();

        _server.Server.Disconnect(7);
        _server.Server.Disconnect(8);

        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);
    }

    [Fact]
    public void InputsSentBeforeTheStartSignalAreDiscarded()
    {
        E2EMatchControl control = new();
        using TestServer server = new(control: control);
        server.Connect(7, "alice");
        server.Receive(7, new SetReadyMsg(true));
        server.AdvanceWithoutReady();

        server.Server.Inputs(7,
            InputPacket.Encode([(42, new PlayerInput(InputButtons.RIGHT))]));
        server.Ready(7);

        WorldStateOutcome state = default;
        control.ReadState(value => state = value);
        server.AdvanceWithoutReady();

        Assert.Equal(8, Assert.Single(state.Players).Position.X);
    }

    private void BeginLoadingMatch()
    {
        _server.Connect(7, "alice");
        _server.Connect(8, "bob");
        _server.Receive(7, new SetReadyMsg(true));
        _server.Receive(8, new SetReadyMsg(true));
        _server.AdvanceWithoutReady();
        Assert.Equal(ServerPhaseKind.MATCH, _server.Server.Phase);
    }
}
