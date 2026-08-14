#if TOOLS
using Godot;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Mortz.Shared.E2E;

namespace Mortz.Server.E2E;

/// <summary>The server end of the control protocol.</summary>
public partial class ServerE2EHandler : Node, IE2EHandler, IMatchObserver
{
    private E2EDriver _driver = null!;

    // Field-initialized so nothing depends on tree order. Real objects under
    // E2E, null objects otherwise. ServerPump reads them without a flag check.
    private E2EMatchControl _control = null!;
    private readonly Dictionary<int, string> _names = [];

    private IE2EResponder? _responder;
    private ServerPhaseKind _phase = ServerPhaseKind.LOBBY;

    public E2EProcessRole Role => E2EProcessRole.SERVER;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(E2EDriver driver, E2EMatchControl control)
    {
        _driver = driver;
        _control = control;
    }

    public override void _Ready()
    {
        _responder = _driver.Responder;
        _driver.Attach(this);
    }

    /// <summary>Called by ServerMain once the transport is bound, with the ports
    /// the OS actually handed out.</summary>
    public void OnListening(int gamePort, int queryPort) =>
        _responder?.Emit(new ServerListeningEvent(gamePort, queryPort));

    public void Handle(E2ERequest request, IE2EResponder responder)
    {
        switch (request)
        {
            case ServerStateRequest state:
                ReadState(state, responder);
                return;
            case MatchSetupRequest setup:
                InMatch(setup.Id, responder, () => _control.ReadSetup(
                    outcome => responder.Respond(new MatchSetupResponse(
                        setup.Id, outcome.Config, outcome.TerrainWidth, outcome.TerrainHeight))));
                return;
            case PlacePlayerRequest place:
                InMatch(place.Id, responder, () => _control.PlacePlayer(
                    place.PeerId, place.Position,
                    outcome => Complete(place.Id, outcome, responder)));
                return;
            case DamagePlayerRequest damage:
                InMatch(damage.Id, responder, () => _control.DamagePlayer(
                    damage.PeerId, damage.Amount,
                    outcome => Complete(damage.Id, outcome, responder)));
                return;
            default:
                responder.Respond(new CommandFailedResponse(request.Id,
                    E2EError.UNKNOWN_COMMAND,
                    $"The server has no handler for {request.GetType().Name}."));
                return;
        }
    }

    // ---- IMatchObserver ----

    public void PlayerJoined(Player player, ServerPhaseKind phase)
    {
        _names[player.PeerId] = player.Name;
        _responder?.Emit(new PlayerJoinedEvent(player.PeerId, player.Name, Map(phase)));
    }

    public void PlayerLeft(Player player, ServerPhaseKind phase)
    {
        _names.Remove(player.PeerId);
        _responder?.Emit(new PlayerLeftEvent(player.PeerId, Map(phase)));
    }

    public void PhaseChanged(ServerPhaseKind kind)
    {
        _phase = kind;
        _responder?.Emit(new PhaseChangedEvent(Map(kind)));
    }

    public void MatchAdvanced(MatchUpdate update)
    {
        if (_responder is not IE2EResponder responder)
            return;
        if (update.Tick % SimConfig.TICK_RATE == 0)
            responder.Emit(new MatchTickEvent(update.Tick));
        foreach (ScoredKill kill in update.Eliminations)
        {
            responder.Emit(new EliminationEvent(update.Tick,
                kill.Score.KillerId, kill.Score.VictimId,
                kill.Score.Kind == DeathKind.SUICIDE));
        }
        if (update.MatchEnded is Victor victor)
            responder.Emit(Ended(update.Tick, victor));
    }

    // ---- translation ----

    private void ReadState(ServerStateRequest request, IE2EResponder responder)
    {
        if (_phase != ServerPhaseKind.MATCH)
        {
            responder.Respond(new ServerStateResponse(request.Id, E2EPhase.LOBBY, 0,
                _names.Select(pair => new E2EServerPlayer(
                    pair.Key, pair.Value, Vec2.Zero, Vec2.Zero, 0, 0)).ToArray()));
            return;
        }
        _control.ReadState(outcome => responder.Respond(new ServerStateResponse(
            request.Id, E2EPhase.MATCH, outcome.Tick,
            outcome.Players.Select(player => new E2EServerPlayer(
                player.PeerId, Name(player.PeerId), player.Position, player.Velocity,
                player.Health, player.RespawnTicks)).ToArray())));
    }

    /// <summary>A mutation outside a match never reaches a tick boundary.
    /// Refused instead of hanging.</summary>
    private void InMatch(Guid id, IE2EResponder responder, Action enqueue)
    {
        if (_phase != ServerPhaseKind.MATCH)
        {
            responder.Respond(new CommandFailedResponse(id, E2EError.INVALID_STATE,
                "The server is not in a match."));
            return;
        }
        enqueue();
    }

    private static void Complete(Guid id, MutationOutcome outcome, IE2EResponder responder)
    {
        if (outcome.Error is string error)
        {
            responder.Respond(new CommandFailedResponse(id, E2EError.INVALID_STATE, error));
            return;
        }
        responder.Respond(new PlayerPlacedResponse(id, outcome.AppliedTick));
    }

    private static void Complete(Guid id, DamageOutcome outcome, IE2EResponder responder)
    {
        if (outcome.Error is string error)
        {
            responder.Respond(new CommandFailedResponse(id, E2EError.INVALID_STATE, error));
            return;
        }
        responder.Respond(new PlayerDamagedResponse(
            id, outcome.RemainingHealth, outcome.Died, outcome.AppliedTick));
    }

    private static MatchEndedEvent Ended(int tick, Victor victor) => victor switch
    {
        Victor.Player player => new MatchEndedEvent(tick, E2EVictorKind.PLAYER, player.PeerId),
        Victor.Team team => new MatchEndedEvent(tick, E2EVictorKind.TEAM, (int)team.Value),
        _ => new MatchEndedEvent(tick, E2EVictorKind.NOBODY, 0),
    };

    private new string Name(int peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";

    private static E2EPhase Map(ServerPhaseKind kind) =>
        kind == ServerPhaseKind.MATCH ? E2EPhase.MATCH : E2EPhase.LOBBY;
}
#endif
