using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Mortz.Server.Admin;
using Mortz.Server.Chat;
using Mortz.Server.Content;
using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Pings;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Mortz.Server.Settings;
using Mortz.Server.Wins;
using Serilog;

namespace Mortz.Server;

/// <summary>The whole dedicated server: Connect, Disconnect, Receive, Inputs, Advance, Dispose.</summary>
public sealed class GameServer : IDisposable, IHandle<Player, PhaseReadyMsg>
{
    private readonly ServerBoot _boot;
    private readonly ReadyLink _link;
    private readonly ILogger _log;
    private readonly IMatchObserver _matchObserver;
    private readonly IMatchControl _matchControl;
    private readonly ServerClock _clock;
    private readonly CurrentPhase _current;
    private readonly PhaseControl _control;
    private readonly Roster _roster;
    private readonly NetRouter<Player> _router = new();
    private readonly HashSet<ushort> _undispatched = [];
    private readonly HashSet<int> _jipAwaitingReady = [];
    private readonly HashSet<int> _matchLoadingPeers = [];
    private readonly object[] _services;
    private readonly SettingsService _settings;
    private readonly AdminService _admin;
    private readonly ChatService _chat;
    private readonly WinsService _wins;

    private IObservePlayers[] _observePlayers = [];
    private IObservePhase[] _observePhase = [];
    private IAdvance[] _advance = [];
    private ISyncJip[] _syncJip = [];

    private ServerPhase _phase;
    private int _generation;
    private bool _matchRunning;
    private bool _disposed;

    public GameServer(ServerBoot boot, IServerTransport transport, IMapSource maps, ILogger log,
        IMatchObserver observer, IMatchControl control)
    {
        _boot = boot;
        _link = new ReadyLink(transport);
        _log = log;
        _matchObserver = observer;
        _matchControl = control;

        _generation = 1;
        var slots = new ServerStateKeys(_generation);
        _clock = new ServerClock();
        _current = new CurrentPhase();
        _control = new PhaseControl();
        _roster = new Roster(slots);

        _settings = new SettingsService(boot, maps, _link, log);
        _admin = new AdminService(slots, _link, _clock, _current, log, boot.AdminPassword);
        _chat = new ChatService(slots, _link, _clock, new Random(boot.Seed));
        TypingService typing = new(slots, _link);
        _wins = new WinsService(slots, _roster, _link, log);
        PingService pings = new(_link);
        EndMatchService endMatch = new(_admin, _chat, _current, _control);
        _services = [_settings, _admin, _chat, typing, _wins, pings, endMatch];

        _phase = LobbyPhase.Open(_roster, _settings, _admin, _chat, _link, log, _control,
            ++_generation);
        Recompose();
    }

    public ServerPhaseKind Phase => _current.Kind;

    public int PlayerCount => _roster.Count;

    public void Connect(int peerId, string requestedName, int requestedSkin = 0)
    {
        _link.BeginLoading(peerId, _generation, _clock.Ms);
        Player player = _roster.Join(peerId, requestedName, requestedSkin);
        _phase.OpenPhaseKeys(player);
        for (int i = 0; i < _observePlayers.Length; i++)
        {
            _observePlayers[i].PlayerJoined(player);
        }
        _phase.PlayerJoined(player);
        _matchObserver.PlayerJoined(player, _current.Kind);
        if (_phase.Kind == ServerPhaseKind.LOBBY)
            _link.Send(peerId, new LobbyLoadMsg(_generation));
        else
        {
            _jipAwaitingReady.Add(peerId);
            _phase.Load(player, _generation, initialPhase: false);
        }
    }

    public void Disconnect(int peerId)
    {
        // Out of the roster before the fan-out, so tallies and broadcasts exclude
        // them; their name and state stay readable until Close below.
        if (_roster.Leave(peerId) is not Player player)
            return;
        _link.Remove(peerId);
        _jipAwaitingReady.Remove(peerId);
        bool wasBlockingMatchStart = _matchLoadingPeers.Remove(peerId);
        _phase.PlayerLeft(player);
        for (int i = _observePlayers.Length - 1; i >= 0; i--)
        {
            _observePlayers[i].PlayerLeft(player);
        }
        _matchObserver.PlayerLeft(player, _current.Kind);
        player.Close(_phase.Kind);
        if (wasBlockingMatchStart)
            TryStartLoadedMatch();
    }

    public void Receive(int peerId, ushort msgId, byte[] payload)
    {
        if (_roster.Find(peerId) is not Player player)
            return;
        if (_router.Dispatch(msgId, player, payload))
            return;
        // A client can legitimately race a phase change, so this is noise, not an error.
        if (_undispatched.Add(msgId))
            _log.Information("no handler for {MessageName}", NetRegistry.NameOf(msgId));
    }

    public void Handle(Player player, in PhaseReadyMsg message)
    {
        if (!_link.Ready(player.PeerId, message.Generation))
            return;
        if (_matchLoadingPeers.Remove(player.PeerId))
        {
            TryStartLoadedMatch();
            return;
        }
        if (_jipAwaitingReady.Remove(player.PeerId))
        {
            for (int i = 0; i < _syncJip.Length; i++)
            {
                _syncJip[i].Sync(player);
            }

            if (_matchRunning)
                _link.Send(player.PeerId, new MatchStartMsg(_generation));
        }
    }

    public void Inputs(int peerId, byte[] packet)
    {
        if (_phase.Kind == ServerPhaseKind.MATCH && !_matchRunning)
            return;
        if (_roster.Find(peerId) is not Player player)
            return;
        _phase.Inputs(player, packet);
    }

    public void Advance(ServerTime time)
    {
        _clock.Ms = time.Ms;
        _link.DisconnectExpired(time.Ms);
        for (int i = 0; i < _advance.Length; i++)
        {
            _advance[i].Advance(time);
        }

        PhaseRequest request = _phase.Kind == ServerPhaseKind.MATCH && !_matchRunning
            ? PhaseRequest.NONE
            : _phase.Advance(time);
        PhaseRequest raised = _control.Take();
        if (request == PhaseRequest.NONE)
            request = raised;

        switch (request)
        {
            case PhaseRequest.START_MATCH:
                StartMatch();
                break;
            case PhaseRequest.RETURN_TO_LOBBY:
                ReturnToLobby();
                break;
        }
    }

    public ServerInfo Describe() => new(
        _boot.Name,
        _settings.ModeName,
        _settings.Map.DisplayName,
        _roster.Count,
        NetConfig.MAX_PLAYERS,
        Phase == ServerPhaseKind.LOBBY,
        _boot.AllowJoinInProgress,
        _boot.GamePort,
        NetConfig.PROTOCOL_VERSION,
        NetRegistry.SCHEMA_HASH);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // Cells first, while every service is still alive and every payload is still readable.
        foreach (Player player in _roster)
        {
            player.Close(_phase.Kind);
        }
        DisposePhase();
        DisposeReverse(_services);
    }

    private void StartMatch()
    {
        // A ready-then-unready in the same tick would otherwise start a broken
        // match.
        if (_phase is not LobbyPhase lobby || !lobby.CanStart)
            return;
        SeatAssignment[] seats = lobby.Seats;
        _log.Information("all {Players} player(s) ready, starting match", seats.Length);
        foreach (Player player in _roster)
        {
            player.CloseLobby();
        }
        EnterPhase(MatchPhase.Open(seats,
            _settings, _wins, _roster, _link, _log, _boot.NetStats, ++_generation,
            _matchObserver, _matchControl, _boot.AllowJoinInProgress));
    }

    private void ReturnToLobby()
    {
        if (_phase is not MatchPhase)
            return;
        foreach (Player player in _roster)
        {
            player.CloseMatch();
        }
        _log.Information("back to lobby ({Players} player(s))", _roster.Count);
        EnterPhase(LobbyPhase.Open(_roster, _settings, _admin, _chat, _link, _log, _control,
            ++_generation));
    }

    /// <summary>Open the next phase for everyone already connected.</summary>
    private void EnterPhase(ServerPhase next)
    {
        _jipAwaitingReady.Clear();
        _matchLoadingPeers.Clear();
        _matchRunning = next.Kind != ServerPhaseKind.MATCH;
        DisposePhase();
        _phase = next;
        foreach (Player player in _roster)
        {
            _link.BeginLoading(player.PeerId, _generation, _clock.Ms);
            if (next.Kind == ServerPhaseKind.MATCH)
                _matchLoadingPeers.Add(player.PeerId);
            next.OpenPhaseKeys(player);
        }
        next.Begin();
        _current.Kind = next.Kind;
        Recompose();
        foreach (Player player in _roster)
        {
            if (next.Kind == ServerPhaseKind.LOBBY)
                _link.Send(player.PeerId, new LobbyLoadMsg(_generation));
            else
                next.Load(player, _generation, initialPhase: true);
        }
        for (int i = 0; i < _observePhase.Length; i++)
        {
            _observePhase[i].PhaseChanged(next.Kind);
        }
        _matchObserver.PhaseChanged(next.Kind);
    }

    private void TryStartLoadedMatch()
    {
        if (_phase.Kind != ServerPhaseKind.MATCH || _matchRunning ||
            _matchLoadingPeers.Count > 0)
            return;
        if (_roster.Count == 0)
        {
            ReturnToLobby();
            return;
        }
        _matchRunning = true;
        _link.Broadcast(new MatchStartMsg(_generation));
        _log.Information("all transition players loaded; match starting");
    }

    private void Recompose()
    {
        object[] live = [.. _services, .. _phase.Services];
        _observePlayers = [.. live.OfType<IObservePlayers>()];
        _observePhase = [.. live.OfType<IObservePhase>()];
        _advance = [.. live.OfType<IAdvance>()];
        _syncJip = [.. _phase.Services.OfType<ISyncJip>()];

        _router.Clear();
        _router.Add(this);
        foreach (object service in live)
        {
            _router.Add(service);
        }
        _log.Information("{Routes}", _router.Describe());
    }

    private static void DisposeReverse(IReadOnlyList<object> services)
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private void DisposePhase()
    {
        DisposeReverse(_phase.Services);
        _phase.Dispose();
    }
}
