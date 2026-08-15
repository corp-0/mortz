#if TOOLS
using Godot;
using Mortz.Client.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.Net;
using Mortz.Shared.E2E;

namespace Mortz.Client.E2E;

/// <summary>The client end of the control protocol. Readiness goes out as the
/// real SetReadyMsg; input plans ride the real prediction path, this node
/// scripts the player, never bypasses it.</summary>
public partial class ClientE2EHandler : Node, IE2EHandler, IE2EClientBridge,
    IHandle<LobbyStateMsg>,
    IHandle<EliminationMsg>,
    IHandle<MatchEndMsg>
{
    /// <summary>Emits the first snapshot, then every 15th. Enough to anchor a
    /// convergence assertion without a line per tick.</summary>
    private const int SNAPSHOT_STRIDE = 15;

    private E2EDriver _driver = null!;

    private NetworkManager _network = null!;
    private IE2EResponder? _responder;
    private GameView? _view;
    private LocalPlayerController? _player;
    private BotInputScript? _script;
    private Snapshot? _lastSnapshot;
    private int _lastSequence = -1;
    private int _lastEmittedTick = int.MinValue;
    private byte _aim;

    public E2EProcessRole Role => E2EProcessRole.CLIENT;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(E2EDriver driver, NetworkManager network)
    {
        _driver = driver;
        _network = network;
    }

    public override void _Ready()
    {
        if (!E2ELaunch.Enabled)
            return;
        _responder = _driver.Responder;
        _network.Connected += OnConnected;
        _network.ConnectionFailed += OnConnectionFailed;
        _network.Disconnected += OnDisconnected;
        _network.Router.Add(this);
        _driver.Attach(this);
    }

    public override void _ExitTree()
    {
        if (!E2ELaunch.Enabled)
            return;
        _network.Connected -= OnConnected;
        _network.ConnectionFailed -= OnConnectionFailed;
        _network.Disconnected -= OnDisconnected;
        _network.Router.Remove(this);
    }

    // ---- IE2EClientBridge ----

    public void AttachMatch(GameView view, LocalPlayerController player)
    {
        _view = view;
        _player = player;
        _lastSnapshot = null;
        _lastEmittedTick = int.MinValue;
        view.SnapshotApplied += OnSnapshot;
        _responder?.Emit(new MatchEnteredEvent(view.MapId));
    }

    public void DetachMatch()
    {
        _view!.SnapshotApplied -= OnSnapshot;
        Uninstall();
        _view = null;
        _player = null;
        _lastSnapshot = null;
    }

    // ---- IE2EHandler ----

    public void Handle(E2ERequest request, IE2EResponder responder)
    {
        switch (request)
        {
            case SetReadyRequest ready:
                new SetReadyMsg(ready.Ready).SendToServer(_network);
                responder.Respond(new ReadySentResponse(ready.Id));
                return;
            case RunInputPlanRequest run:
                RunPlan(run, responder);
                return;
            case ClientStateRequest state:
                ReadState(state, responder);
                return;
            default:
                responder.Respond(new CommandFailedResponse(request.Id,
                    E2EError.UNKNOWN_COMMAND,
                    $"The client has no handler for {request.GetType().Name}."));
                return;
        }
    }

    private void RunPlan(RunInputPlanRequest request, IE2EResponder responder)
    {
        if (_player is not LocalPlayerController player)
        {
            responder.Respond(new CommandFailedResponse(request.Id, E2EError.INVALID_STATE,
                "The client is not in a match."));
            return;
        }

        if (_script is BotInputScript replaced)
            _responder?.Emit(new InputPlanCancelledEvent(replaced.PlanId, _lastSequence));
        BotInputScript script = new(request.Plan, player.NextSeq);
        _script = script;
        _lastSequence = script.FirstSequence - 1;
        player.ButtonFilter = Filter;
        player.AimProvider = () => _aim;
        responder.Respond(new InputPlanAcceptedResponse(request.Id, script.PlanId));
        _responder?.Emit(new InputPlanStartedEvent(script.PlanId, script.FirstSequence));
    }

    private void ReadState(ClientStateRequest request, IE2EResponder responder)
    {
        if (_player is not LocalPlayerController player || _lastSnapshot is not Snapshot snapshot)
        {
            responder.Respond(new CommandFailedResponse(request.Id, E2EError.INVALID_STATE,
                "The client has no applied snapshot yet."));
            return;
        }
        responder.Respond(new ClientStateResponse(
            request.Id, snapshot.Tick, player.State.Position, Observed(snapshot)));
    }

    // ---- scripted input ----

    /// <summary>Called once per physics tick. Keeps the plan aligned with the
    /// sequence numbers it was installed against.</summary>
    private InputButtons Filter(int sequence, InputButtons sampled)
    {
        if (_script is not BotInputScript script)
            return sampled;
        _lastSequence = sequence;
        if (script.IsDone(sequence))
        {
            Uninstall();
            _responder?.Emit(new InputPlanCompletedEvent(script.PlanId, script.LastSequence));
            return sampled;
        }
        _aim = script.AimAt(sequence);
        return script.ButtonsAt(sequence);
    }

    private void Uninstall()
    {
        _script = null;
        if (_player is not LocalPlayerController player)
            return;
        player.ButtonFilter = null;
        player.AimProvider = null;
    }

    // ---- observation ----

    private void OnConnected() => _responder?.Emit(new ConnectedEvent(_network.LocalPeerId));

    private void OnConnectionFailed() => _responder?.Emit(new ConnectionFailedEvent());

    private void OnDisconnected() => _responder?.Emit(new DisconnectedEvent());

    public void Handle(in LobbyStateMsg message) =>
        _responder?.Emit(new LobbyRosterObservedEvent(message.Members.Length));

    public void Handle(in EliminationMsg message) =>
        _responder?.Emit(new EliminationObservedEvent(
            _lastSnapshot?.Tick ?? -1,
            message.KillerId,
            message.VictimId,
            message.Flags.HasFlag(EliminationFlags.SUICIDE)));

    public void Handle(in MatchEndMsg message)
    {
        if (!MatchProtocol.TryDecode(message, out Victor? victor))
            return;
        _responder?.Emit(victor switch
        {
            Victor.Player player => new MatchEndObservedEvent(E2EVictorKind.PLAYER, player.PeerId),
            Victor.Team team => new MatchEndObservedEvent(E2EVictorKind.TEAM, (int)team.Value),
            _ => new MatchEndObservedEvent(E2EVictorKind.NOBODY, 0),
        });
    }

    private void OnSnapshot(Snapshot snapshot)
    {
        _lastSnapshot = snapshot;
        if (_lastEmittedTick != int.MinValue &&
            snapshot.Tick - _lastEmittedTick < SNAPSHOT_STRIDE)
            return;
        _lastEmittedTick = snapshot.Tick;
        _responder?.Emit(new SnapshotObservedEvent(snapshot.Tick, Observed(snapshot)));
    }

    private static E2EObservedPlayer[] Observed(Snapshot snapshot) =>
        snapshot.Players
            .Select(player => new E2EObservedPlayer(
                player.PeerId, player.Position, player.Health))
            .ToArray();
}
#endif
