using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Xunit;

namespace Mortz.E2E.Tests;

public sealed class ProtocolContractTests
{
    private static readonly Guid _requestId = Guid.NewGuid();
    private static readonly Guid _planId = Guid.NewGuid();

    private static BotInputPlan SamplePlan() => BotInputPlan.Sequence(
        new BotInputFrame(InputButtons.RIGHT, 17, 12),
        new BotInputFrame(InputButtons.JUMP | InputButtons.FIRE, 99, 1));

    private static E2ERequest[] Requests() =>
    [
        new PingRequest(_requestId, E2EProtocolSchema.Hash),
        new ShutdownRequest(_requestId),
        new ServerStateRequest(_requestId),
        new MatchSetupRequest(_requestId),
        new PlacePlayerRequest(_requestId, 7, new Vec2(320, 180)),
        new DamagePlayerRequest(_requestId, 7, 40),
        new SetReadyRequest(_requestId, true),
        new RunInputPlanRequest(_requestId, SamplePlan()),
        new ClientStateRequest(_requestId),
    ];

    private static E2EResponse[] Responses() =>
    [
        new PongResponse(_requestId, E2EProtocolSchema.Hash),
        new ShutdownStartedResponse(_requestId),
        new ServerStateResponse(_requestId, E2EPhase.MATCH, 42,
            [new E2EServerPlayer(7, "alice", new Vec2(1, 2), Vec2.Zero, 100, 0)]),
        new MatchSetupResponse(_requestId, new MatchConfig().ToBytes(), 1280, 720),
        new PlayerPlacedResponse(_requestId, 43),
        new PlayerDamagedResponse(_requestId, 60, false, 44),
        new ReadySentResponse(_requestId),
        new InputPlanAcceptedResponse(_requestId, _planId),
        new ClientStateResponse(_requestId, 44, new Vec2(3, 4),
            [new E2EObservedPlayer(7, new Vec2(1, 2), 100)]),
        new CommandFailedResponse(_requestId, E2EError.INVALID_STATE, "not ready"),
    ];

    private static E2EEvent[] Events() =>
    [
        new ProcessReadyEvent(E2EProcessRole.CLIENT, 42, E2EProtocolSchema.Hash),
        new ServerListeningEvent(51234, -1),
        new PhaseChangedEvent(E2EPhase.MATCH),
        new PlayerJoinedEvent(7, "alice", E2EPhase.LOBBY),
        new PlayerLeftEvent(7, E2EPhase.MATCH),
        new MatchTickEvent(120),
        new EliminationEvent(120, 7, 8, false),
        new MatchEndedEvent(180, E2EVictorKind.PLAYER, 7),
        new ConnectedEvent(7),
        new ConnectionFailedEvent(),
        new DisconnectedEvent(),
        new LobbyRosterObservedEvent(2),
        new MatchEnteredEvent("castlewars"),
        new SnapshotObservedEvent(60, [new E2EObservedPlayer(7, new Vec2(1, 2), 100)]),
        new EliminationObservedEvent(61, 7, 8, true),
        new MatchEndObservedEvent(E2EVictorKind.TEAM, 1),
        new InputPlanStartedEvent(_planId, 100),
        new InputPlanCompletedEvent(_planId, 120),
        new InputPlanCancelledEvent(_planId, 110),
    ];

    [Fact]
    public void EveryRequestRoundTripsThroughGeneratedWire()
    {
        E2ERequest[] samples = Requests();

        AssertRegisteredTypes("request", samples.Select(value => value.GetType()));
        foreach (E2ERequest sample in samples)
        {
            E2ERequest decoded = E2EWire.DeserializeRequest(E2EWire.Serialize(sample));
            Assert.Equal(sample.GetType(), decoded.GetType());
            Assert.Equal(sample.Id, decoded.Id);
        }
    }

    [Fact]
    public void InputPlanSurvivesTheWireWithItsRealButtonBits()
    {
        BotInputPlan plan = SamplePlan();

        RunInputPlanRequest decoded = Assert.IsType<RunInputPlanRequest>(
            E2EWire.DeserializeRequest(
                E2EWire.Serialize(new RunInputPlanRequest(_requestId, plan))));

        Assert.Equal(plan.Id, decoded.Plan.Id);
        Assert.Equal(plan.Frames, decoded.Plan.Frames);
        Assert.Equal(InputButtons.JUMP | InputButtons.FIRE, decoded.Plan.Frames[1].Buttons);
    }

    [Fact]
    public void PlacementSurvivesTheWireWithItsPosition()
    {
        PlacePlayerRequest decoded = Assert.IsType<PlacePlayerRequest>(
            E2EWire.DeserializeRequest(E2EWire.Serialize(
                new PlacePlayerRequest(_requestId, 7, new Vec2(320.5f, 180.25f)))));

        Assert.Equal(7, decoded.PeerId);
        Assert.Equal(new Vec2(320.5f, 180.25f), decoded.Position);
    }

    [Fact]
    public void EveryResponseAndEventRoundTripsThroughGeneratedWire()
    {
        E2EResponse[] responses = Responses();
        E2EEvent[] events = Events();

        AssertRegisteredTypes("response", responses.Select(value => value.GetType()));
        AssertRegisteredTypes("event", events.Select(value => value.GetType()));

        foreach (E2EResponse response in responses)
        {
            E2EMessage decoded = E2EWire.DeserializeMessage(
                E2EWire.Serialize(new ResponseMessage(response)));
            ResponseMessage message = Assert.IsType<ResponseMessage>(decoded);
            Assert.Equal(response.GetType(), message.Response.GetType());
            Assert.Equal(response.Id, message.Response.Id);
        }
        foreach (E2EEvent raised in events)
        {
            E2EMessage decoded = E2EWire.DeserializeMessage(
                E2EWire.Serialize(new EventMessage(raised)));
            EventMessage message = Assert.IsType<EventMessage>(decoded);
            Assert.Equal(raised.GetType(), message.Event.GetType());
        }
    }

    [Fact]
    public void ServerStateCarriesItsPlayersAcrossTheWire()
    {
        ServerStateResponse sample = new(_requestId, E2EPhase.MATCH, 42,
            [new E2EServerPlayer(7, "alice", new Vec2(1, 2), new Vec2(3, -4), 90, 3)]);

        ResponseMessage decoded = Assert.IsType<ResponseMessage>(
            E2EWire.DeserializeMessage(E2EWire.Serialize(new ResponseMessage(sample))));

        ServerStateResponse state = Assert.IsType<ServerStateResponse>(decoded.Response);
        E2EServerPlayer player = Assert.Single(state.Players);
        Assert.Equal(new E2EServerPlayer(7, "alice", new Vec2(1, 2), new Vec2(3, -4), 90, 3), player);
    }

    [Fact]
    public void EveryRequestDeclaresExactlyOneResponse()
    {
        ProtocolRegistration[] requests = E2EProtocolSchema.Registrations
            .Where(value => value.Family == "request")
            .ToArray();

        Assert.NotEmpty(requests);
        Assert.All(requests, request => Assert.NotNull(request.ResponseType));
        Type[] responses = E2EProtocolSchema.Registrations
            .Where(value => value.Family == "response")
            .Select(value => value.Type)
            .ToArray();
        Assert.All(requests, request => Assert.Contains(request.ResponseType!, responses));
        Assert.Equal(
            requests.Length,
            requests.Select(value => value.Discriminator).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryRequestDeclaresTheRolesThatAcceptIt()
    {
        Type[] requests = E2EProtocolSchema.Registrations
            .Where(value => value.Family == "request")
            .Select(value => value.Type)
            .ToArray();

        Assert.All(requests, request => Assert.True(
            typeof(IServerRequest).IsAssignableFrom(request) ||
            typeof(IClientRequest).IsAssignableFrom(request),
            $"{request.Name} declares neither IServerRequest nor IClientRequest."));
    }

    [Fact]
    public void RoleMarkersMatchTheOwningProcess()
    {
        Assert.True(typeof(IServerRequest).IsAssignableFrom(typeof(PlacePlayerRequest)));
        Assert.False(typeof(IClientRequest).IsAssignableFrom(typeof(PlacePlayerRequest)));
        Assert.True(typeof(IClientRequest).IsAssignableFrom(typeof(RunInputPlanRequest)));
        Assert.False(typeof(IServerRequest).IsAssignableFrom(typeof(RunInputPlanRequest)));
        Assert.True(typeof(IServerRequest).IsAssignableFrom(typeof(ShutdownRequest)));
        Assert.True(typeof(IClientRequest).IsAssignableFrom(typeof(ShutdownRequest)));
    }

    [Fact]
    public void SchemaHashIsStableAndSha256Sized()
    {
        string first = E2EProtocolSchema.Hash;
        string second = E2EProtocolSchema.Hash;

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void PressPlanIncludesTheReleaseEdge()
    {
        BotInputPlan plan = BotInputPlan.Press(InputButtons.JUMP, aim: 37);

        Assert.Equal(2, plan.Frames.Count);
        Assert.Equal(InputButtons.JUMP, plan.Frames[0].Buttons);
        Assert.Equal(InputButtons.NONE, plan.Frames[1].Buttons);
        Assert.All(plan.Frames, frame => Assert.Equal((byte)37, frame.Aim));
    }

    [Fact]
    public void PlanOwnsItsFrameArray()
    {
        BotInputFrame original = new(InputButtons.PARRY, 0, 1);
        BotInputFrame[] frames = [original];

        BotInputPlan plan = BotInputPlan.Sequence(frames);
        frames[0] = new BotInputFrame(InputButtons.FIRE, 0, 1);

        Assert.Equal(original, plan.Frames[0]);
    }

    [Fact]
    public void InputFrameRejectsUndefinedButtons()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BotInputFrame((InputButtons)(1 << 15), 0, 1));
    }

    [Fact]
    public void ExitCodesAreDistinct()
    {
        Assert.NotEqual(E2EExitCode.CLEAN, E2EExitCode.STDIN_EOF);
        Assert.Equal(64, E2EExitCode.STDIN_EOF);
    }

    private static void AssertRegisteredTypes(string family, IEnumerable<Type> sampleTypes)
    {
        Type[] expected = E2EProtocolSchema.Registrations
            .Where(value => value.Family == family)
            .Select(value => value.Type)
            .OrderBy(value => value.FullName, StringComparer.Ordinal)
            .ToArray();
        Type[] actual = sampleTypes
            .OrderBy(value => value.FullName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }
}
