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
    private readonly PhaseTransitionCoordinator _host;
    private readonly Roster _roster;
    private readonly NetRouter<Player> _router = new();
    private readonly HashSet<ushort> _undispatched = [];
    private readonly object[] _services;
    private readonly SettingsService _settings;
    private readonly AdminService _admin;
    private readonly ChatService _chat;
    private readonly WinsService _wins;

    private IObservePlayers[] _observePlayers = [];
    private IObservePhase[] _observePhase = [];
    private IAdvance[] _advance = [];
    private ISyncJip[] _syncJip = [];

    private bool _disposed;

    public GameServer(ServerBoot boot, IServerTransport transport, IMapSource maps, ILogger log,
        IMatchObserver observer, IMatchControl control)
    {
        _boot = boot;
        _link = new ReadyLink(transport);
        _log = log;
        _matchObserver = observer;
        _matchControl = control;

        _host = new PhaseTransitionCoordinator(generation: 1);
        var slots = new ServerStateKeys(_host.Generation);
        _clock = new ServerClock();
        _roster = new Roster(slots);

        _settings = new SettingsService(boot, maps, _link, log);
        _admin = new AdminService(slots, _link, _clock, _host, log, boot.AdminPassword);
        _chat = new ChatService(slots, _link, _clock, new Random(boot.Seed));
        TypingService typing = new(slots, _link);
        _wins = new WinsService(slots, _roster, _link, log);
        PingService pings = new(_link);
        EndMatchService endMatch = new(_admin, _chat, _host, _host);
        _services = [_settings, _admin, _chat, typing, _wins, pings, endMatch];

        _host.OpenInitial(LobbyPhase.Open(
            _roster, _settings, _admin, _chat, _link, log, _host));
        Recompose();
    }

    public ServerPhaseKind Phase => _host.Kind;

    public int PlayerCount => _roster.Count;

    public void Connect(int peerId, string requestedName, int requestedSkin = 0)
    {
        _link.BeginLoading(peerId, _host.Generation, _clock.Ms);
        Player player = _roster.Join(peerId, requestedName, requestedSkin);
        _host.OpenPhaseKeys(player);
        for (int i = 0; i < _observePlayers.Length; i++)
        {
            _observePlayers[i].PlayerJoined(player);
        }
        _host.PlayerJoined(player);
        _matchObserver.PlayerJoined(player, _host.Kind);
        Execute(_host.Load(player));
    }

    public void Disconnect(int peerId)
    {
        // Out of the roster before the fan-out, so tallies and broadcasts exclude
        // them; their name and state stay readable until Close below.
        if (_roster.Leave(peerId) is not Player player)
            return;
        _link.Remove(peerId);
        _host.PlayerLeft(player);
        for (int i = _observePlayers.Length - 1; i >= 0; i--)
        {
            _observePlayers[i].PlayerLeft(player);
        }
        _matchObserver.PlayerLeft(player, _host.Kind);
        player.Close(_host.Kind);
        Execute(_host.PlayerDisconnected(peerId, _roster.Count));
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
        Execute(_host.Ready(player, message.Generation, _roster.Count));
    }

    public void Inputs(int peerId, byte[] packet)
    {
        if (!_host.InputsAllowed)
            return;
        if (_roster.Find(peerId) is not Player player)
            return;
        _host.Inputs(player, packet);
    }

    public void Advance(ServerTime time)
    {
        _clock.Ms = time.Ms;
        _link.DisconnectExpired(time.Ms);
        for (int i = 0; i < _advance.Length; i++)
        {
            _advance[i].Advance(time);
        }

        Execute(_host.Advance(time));
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
            player.Close(_host.Kind);
        }
        _host.Dispose();
        DisposeReverse(_services);
    }

    private void StartMatch(IReadOnlyList<SeatAssignment> seats)
    {
        _log.Information("all {Players} player(s) ready, starting match", seats.Count);
        EnterPhase(MatchPhase.Open(seats,
            _settings, _wins, _roster, _link, _log, _boot.NetStats, _host.NextGeneration,
            _matchObserver, _matchControl, _boot.AllowJoinInProgress));
    }

    private void ReturnToLobby()
    {
        if (_host.Kind != ServerPhaseKind.MATCH)
            return;
        foreach (Player player in _roster)
        {
            player.CloseMatch();
        }
        _log.Information("back to lobby ({Players} player(s))", _roster.Count);
        EnterPhase(LobbyPhase.Open(_roster, _settings, _admin, _chat, _link, _log, _host));
    }

    /// <summary>Open the next phase for everyone already connected.</summary>
    private void EnterPhase(ServerPhase next)
    {
        Player[] players = [.. _roster];
        int generation = _host.NextGeneration;
        foreach (Player player in players)
        {
            _link.BeginLoading(player.PeerId, generation, _clock.Ms);
        }
        IReadOnlyList<PhaseHostAction> loads = _host.TransitionTo(next, players);
        Recompose();
        Execute(loads);
        for (int i = 0; i < _observePhase.Length; i++)
        {
            _observePhase[i].PhaseChanged(next.Kind);
        }
        _matchObserver.PhaseChanged(next.Kind);
    }

    private void Execute(PhaseHostAction? action)
    {
        if (action == null)
            return;
        switch (action)
        {
            case PhaseHostAction.SendLobbyLoad load:
                _link.Send(load.Player.PeerId, new LobbyLoadMsg(load.Generation));
                break;
            case PhaseHostAction.SendMatchLoad load:
                _host.LoadMatch(load.Player, load.Generation, load.Initial);
                break;
            case PhaseHostAction.SyncJip sync:
                for (int i = 0; i < _syncJip.Length; i++)
                {
                    _syncJip[i].Sync(sync.Player);
                }
                break;
            case PhaseHostAction.SendMatchStart start:
                _link.Send(start.Player.PeerId, new MatchStartMsg(start.Generation));
                break;
            case PhaseHostAction.BroadcastMatchStart start:
                _link.Broadcast(new MatchStartMsg(start.Generation));
                _log.Information("all transition players loaded; match starting");
                break;
            case PhaseHostAction.EnterLobby:
                ReturnToLobby();
                break;
            case PhaseHostAction.EnterMatch start:
                StartMatch(start.Seats);
                break;
        }
    }

    private void Execute(IReadOnlyList<PhaseHostAction> actions)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            Execute(actions[i]);
        }
    }

    private void Recompose()
    {
        object[] live = [.. _services, .. _host.Services];
        _observePlayers = [.. live.OfType<IObservePlayers>()];
        _observePhase = [.. live.OfType<IObservePhase>()];
        _advance = [.. live.OfType<IAdvance>()];
        _syncJip = [.. _host.Services.OfType<ISyncJip>()];

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
}
