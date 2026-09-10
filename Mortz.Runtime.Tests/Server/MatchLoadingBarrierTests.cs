using Mortz.Core.Sim;
using Mortz.Protocol.Input;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Lobby;
using Mortz.Server.Diagnostics;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

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
        Assert.Null(_observer.LastUpdate);
        Assert.DoesNotContain(_server.Link.Messages, sent => sent.Message is MatchStartMsg);

        _server.Ready(7);
        _server.AdvanceWithoutReady();
        Assert.Null(_observer.LastUpdate);

        _server.Ready(8);
        Assert.Contains(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
        Assert.Null(_observer.LastUpdate);

        _server.AdvanceWithoutReady();
        Assert.NotNull(_observer.LastUpdate);
    }

    [Fact]
    public void StaleReadyDoesNotReleaseTheBarrier()
    {
        BeginLoadingMatch();
        int generation = _server.Link.Last<MatchLoadMsg>().Generation;

        _server.Receive(7, new PhaseReadyMsg(generation - 1));
        _server.Ready(8);

        Assert.DoesNotContain(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
        Assert.Null(_observer.LastUpdate);
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
        _server.Server.Connect(new(9, "jip", 0, null));

        _server.Ready(7);
        _server.Ready(8);

        Assert.Contains(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
    }

    [Fact]
    public void JipReadySyncsOnlyTheJoinerAndDoesNotReleaseTheLobbyCohort()
    {
        BeginLoadingMatch();
        _server.Server.Connect(new(9, "jip", 0, null));
        int generation = _server.Link.Messages.Select(sent => sent.Message)
            .OfType<MatchLoadMsg>()
            .Last()
            .Generation;
        _server.Link.Messages.Clear();

        _server.Receive(9, new PhaseReadyMsg(generation));

        Assert.NotEmpty(_server.Link.Messages);
        Assert.All(_server.Link.Messages, sent => Assert.Equal(9, sent.Target));
        Assert.DoesNotContain(_server.Link.Messages, sent => sent.Message is MatchStartMsg);

        _server.Receive(7, new PhaseReadyMsg(generation));
        Assert.DoesNotContain(_server.Link.Messages, sent => sent.Message is MatchStartMsg);
        _server.Receive(8, new PhaseReadyMsg(generation));
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
            InputPacket.Encode([(42, new PlayerInput(InputButtons.RIGHT))], server.Server.Generation));
        server.Ready(7);

        WorldStateOutcome state = default;
        control.ReadState(value => state = value);
        server.AdvanceWithoutReady();

        Assert.Equal(8, Assert.Single(state.Players).Position.X);
    }

    [Fact]
    public void PreviousMatchInputsAreRejectedAfterTheNewMatchStarts()
    {
        E2EMatchControl control = new();
        using TestServer server = new(control: control);
        server.Connect(7, "alice");
        server.Receive(7, new SetReadyMsg(true));
        server.Tick();
        server.Server.Inputs(7, InputPacket.Encode([(42, new PlayerInput(InputButtons.RIGHT))],
            server.Server.Generation - 1));
        WorldStateOutcome state = default;
        control.ReadState(value => state = value);
        server.AdvanceWithoutReady();
        Assert.Equal(8, Assert.Single(state.Players).Position.X);
        server.Server.Inputs(7, InputPacket.Encode([(43, new PlayerInput(InputButtons.RIGHT))],
            server.Server.Generation));
        control.ReadState(value => state = value);
        server.AdvanceWithoutReady();
        Assert.True(Assert.Single(state.Players).Position.X > 8);
    }

    [Fact]
    public void MalformedInputPacketsAreRejectedAtTheServerBoundary()
    {
        E2EMatchControl control = new();
        using TestServer server = new(control: control);
        server.Connect(7, "alice");
        server.Receive(7, new SetReadyMsg(true));
        server.Tick();
        byte[] valid = InputPacket.Encode([(42, new PlayerInput(InputButtons.RIGHT))],
            server.Server.Generation);

        for (int length = 0; length < valid.Length; length++)
        {
            server.Server.Inputs(7, valid[..length]);
        }
        server.Server.Inputs(7, [.. valid, 0]);
        byte[] undefinedButtons = (byte[])valid.Clone();
        undefinedButtons[^2] = 0x80;
        server.Server.Inputs(7, undefinedButtons);

        WorldStateOutcome state = default;
        control.ReadState(value => state = value);
        server.AdvanceWithoutReady();
        Assert.Equal(8, Assert.Single(state.Players).Position.X);

        server.Server.Inputs(7, valid);
        control.ReadState(value => state = value);
        server.AdvanceWithoutReady();
        Assert.True(Assert.Single(state.Players).Position.X > 8);
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
