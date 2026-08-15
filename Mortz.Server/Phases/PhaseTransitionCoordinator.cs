using Mortz.Server.Players;

namespace Mortz.Server.Phases;

public enum PhaseLoadPurpose
{
    TRANSITION_COHORT,
    JOIN_IN_PROGRESS,
}

public interface IPhaseTransitionRequests
{
    bool RequestStartMatch();

    bool RequestReturnToLobby();
}

public abstract record PhaseHostAction
{
    public sealed record SendLobbyLoad(Player Player, int Generation) : PhaseHostAction;

    public sealed record SendMatchLoad(Player Player, int Generation, bool Initial) : PhaseHostAction;

    public sealed record SyncJip(Player Player) : PhaseHostAction;

    public sealed record SendMatchStart(Player Player, int Generation) : PhaseHostAction;

    public sealed record BroadcastMatchStart(int Generation) : PhaseHostAction;

    public sealed record EnterLobby : PhaseHostAction;

    public sealed record EnterMatch(IReadOnlyList<SeatAssignment> Seats) : PhaseHostAction;
}

/// <summary>Owns the current phase, its generation, and every phase-loading transition.</summary>
public sealed class PhaseTransitionCoordinator(int generation) : ICurrentPhase, IPhaseTransitionRequests, IDisposable
{
    private readonly Dictionary<int, PhaseLoadPurpose> _loading = [];

    private ServerPhase? _phase;
    private PhaseRequest _pending;
    private bool _matchRunning;
    private bool _disposed;

    public ServerPhaseKind Kind => Phase.Kind;

    public int Generation { get; private set; } = generation;

    public int NextGeneration => checked(Generation + 1);

    public IReadOnlyList<object> Services => Phase.Services;

    public bool InputsAllowed => Kind != ServerPhaseKind.MATCH || _matchRunning;

    private ServerPhase Phase => _phase ??
        throw new InvalidOperationException("the initial phase has not been opened");

    public void OpenInitial(ServerPhase phase)
    {
        if (_phase != null)
            throw new InvalidOperationException("the initial phase is already open");
        Generation = NextGeneration;
        _phase = phase;
        _matchRunning = phase.Kind != ServerPhaseKind.MATCH;
    }

    public PhaseHostAction Load(Player player)
    {
        if (Kind == ServerPhaseKind.LOBBY)
            return new PhaseHostAction.SendLobbyLoad(player, Generation);

        _loading[player.PeerId] = PhaseLoadPurpose.JOIN_IN_PROGRESS;
        return new PhaseHostAction.SendMatchLoad(player, Generation, Initial: false);
    }

    public void OpenPhaseKeys(Player player) => Phase.OpenPhaseKeys(player);

    public void PlayerJoined(Player player) => Phase.PlayerJoined(player);

    public void PlayerLeft(Player player) => Phase.PlayerLeft(player);

    public void Inputs(Player player, byte[] packet) => Phase.Inputs(player, packet);

    public void LoadMatch(Player player, int generation, bool initial) =>
        Phase.Load(player, generation, initial);

    public PhaseHostAction? Advance(ServerTime time)
    {
        PhaseRequest phaseRequest = InputsAllowed ? Phase.Advance(time) : PhaseRequest.NONE;
        PhaseRequest pending = _pending;
        _pending = PhaseRequest.NONE;
        PhaseRequest request = phaseRequest != PhaseRequest.NONE ? phaseRequest : pending;

        return request switch
        {
            PhaseRequest.START_MATCH when Phase is LobbyPhase { CanStart: true } lobby =>
                new PhaseHostAction.EnterMatch(lobby.Seats),
            PhaseRequest.RETURN_TO_LOBBY when Phase is MatchPhase =>
                new PhaseHostAction.EnterLobby(),
            _ => null,
        };
    }

    public bool RequestStartMatch()
    {
        if (Kind != ServerPhaseKind.LOBBY)
            return false;
        _pending = PhaseRequest.START_MATCH;
        return true;
    }

    public bool RequestReturnToLobby()
    {
        if (Kind != ServerPhaseKind.MATCH)
            return false;
        _pending = PhaseRequest.RETURN_TO_LOBBY;
        return true;
    }

    public IReadOnlyList<PhaseHostAction> TransitionTo(
        ServerPhase next, IReadOnlyList<Player> players)
    {
        DisposePhase();
        _phase = next;
        Generation = NextGeneration;
        _pending = PhaseRequest.NONE;
        _loading.Clear();
        _matchRunning = next.Kind != ServerPhaseKind.MATCH;

        foreach (Player player in players)
        {
            if (next.Kind == ServerPhaseKind.MATCH)
                _loading.Add(player.PeerId, PhaseLoadPurpose.TRANSITION_COHORT);
            next.OpenPhaseKeys(player);
        }
        next.Begin();

        PhaseHostAction[] loads = new PhaseHostAction[players.Count];
        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i];
            loads[i] = next.Kind == ServerPhaseKind.LOBBY
                ? new PhaseHostAction.SendLobbyLoad(player, Generation)
                : new PhaseHostAction.SendMatchLoad(player, Generation, Initial: true);
        }
        return loads;
    }

    public IReadOnlyList<PhaseHostAction> Ready(Player player, int generation, int playerCount)
    {
        if (generation != Generation || !_loading.Remove(player.PeerId, out PhaseLoadPurpose purpose))
            return [];

        if (purpose == PhaseLoadPurpose.JOIN_IN_PROGRESS)
        {
            return _matchRunning
                ?
                [
                    new PhaseHostAction.SyncJip(player),
                    new PhaseHostAction.SendMatchStart(player, Generation),
                ]
                : [new PhaseHostAction.SyncJip(player)];
        }

        return ReleaseTransitionCohort(playerCount);
    }

    public IReadOnlyList<PhaseHostAction> PlayerDisconnected(int peerId, int playerCount)
    {
        if (!_loading.Remove(peerId, out PhaseLoadPurpose purpose) ||
            purpose != PhaseLoadPurpose.TRANSITION_COHORT)
            return [];
        return ReleaseTransitionCohort(playerCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposePhase();
    }

    private IReadOnlyList<PhaseHostAction> ReleaseTransitionCohort(int playerCount)
    {
        if (Kind != ServerPhaseKind.MATCH || _matchRunning ||
            _loading.Values.Contains(PhaseLoadPurpose.TRANSITION_COHORT))
            return [];
        if (playerCount == 0)
            return [new PhaseHostAction.EnterLobby()];

        _matchRunning = true;
        return [new PhaseHostAction.BroadcastMatchStart(Generation)];
    }

    private void DisposePhase()
    {
        if (_phase == null)
            return;
        IReadOnlyList<object> services = _phase.Services;
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i] is IDisposable disposable)
                disposable.Dispose();
        }
        _phase.Dispose();
    }
}
