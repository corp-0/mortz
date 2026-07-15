using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Godot lifecycle adapter for the dedicated server. Match policy is
/// owned by <see cref="MatchSession"/> and wire translation by
/// <see cref="ServerProtocol"/>; this node only coordinates transitions and
/// forwards engine/network callbacks.</summary>
public partial class ServerMain : Node
{
    private const float MATCH_END_SECONDS = 6;

    [Export] private string _defaultMap = "castlewars";

    private readonly PlayerDirectory _players = new();
    private MapPackage _map = null!;
    private MatchConfig _rules = null!;
    private LobbySession? _lobby = new();
    private MatchSession? _match;
    private ServerProtocol _protocol = null!;
    private bool _debugCarveEnabled;
    private bool _subscribed;

    public override void _Ready()
    {
        if (!TryLoadConfiguration())
        {
            GetTree().Quit(1);
            return;
        }

        NetworkManager network = NetworkManager.Instance;
        _protocol = new ServerProtocol(network, _map, _players,
            CmdArgs.HasFlag("--net-stats"));
        Subscribe(network);

        int port = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        Error error = network.StartServer(port);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[server] failed to listen on port {port}: {error}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[server] listening on port {port} (protocol v{NetConfig.PROTOCOL_VERSION}, " +
                 $"map '{_map.DisplayName}' {_map.Width}x{_map.Height}, " +
                 $"tick {SimConfig.TICK_RATE} Hz)");
    }

    public override void _ExitTree()
    {
        if (!_subscribed)
            return;
        NetworkManager network = NetworkManager.Instance;
        network.PeerJoined -= OnPeerJoined;
        network.PeerLeft -= OnPeerLeft;
        network.InputsReceived -= OnInputsReceived;
        SetReadyMsg.Received -= OnSetReady;
        if (_debugCarveEnabled)
            DebugCarveMsg.Received -= OnDebugCarve;
        _subscribed = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_match is not { } match)
            return;

        MatchFrame frame = match.Step();
        if (frame.MatchEnded != null)
            Engine.TimeScale = SimConfig.MATCH_END_TIME_SCALE;
        _protocol.Publish(frame, match);
        if (frame.ReturnToLobby)
            ReturnToLobby(match);
    }

    private bool TryLoadConfiguration()
    {
        string mapId = CmdArgs.GetValue("--map") ?? _defaultMap;
        MapPackage? map = MapPackage.Load(mapId);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            return false;
        }
        MatchConfig? rules = LoadRuleset();
        ServerConfig? serverConfig = ServerConfig.Load();
        if (rules == null || serverConfig == null)
            return false;

        string adminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (adminPassword.Length > 0)
            GD.Print("[server] admin password set");
        _map = map;
        _rules = rules;
        return true;
    }

    private void Subscribe(NetworkManager network)
    {
        network.PeerJoined += OnPeerJoined;
        network.PeerLeft += OnPeerLeft;
        network.InputsReceived += OnInputsReceived;
        SetReadyMsg.Received += OnSetReady;
        _debugCarveEnabled = CmdArgs.HasFlag("--enable-debug-carve");
        if (_debugCarveEnabled)
        {
            DebugCarveMsg.Received += OnDebugCarve;
            GD.Print("[server] debug carve enabled");
        }
        _subscribed = true;
    }

    private void OnPeerJoined(long peerId, string requestedName)
    {
        string name = _players.Add(peerId, requestedName);
        if (_match is { } match)
        {
            AddToMatch(peerId, match);
            _protocol.BroadcastRoster(match);
            return;
        }

        LobbySession lobby = _lobby!;
        lobby.Add(peerId);
        GD.Print($"[server] player {peerId} '{name}' entered lobby ({lobby.Count} waiting)");
        _protocol.BroadcastLobby(lobby);
    }

    private void OnPeerLeft(long peerId)
    {
        _players.Remove(peerId);
        if (_match is { } match)
        {
            match.RemovePlayer((int)peerId);
            GD.Print($"[server] player {peerId} left ({match.World.Players.Count} in game)");
            _protocol.BroadcastRoster(match);
            return;
        }

        LobbySession lobby = _lobby!;
        if (!lobby.Remove(peerId))
            return;
        GD.Print($"[server] player {peerId} left lobby ({lobby.Count} waiting)");
        _protocol.BroadcastLobby(lobby);
        TryStartMatch();
    }

    private void OnSetReady(long sender, SetReadyMsg message)
    {
        if (_lobby is not { } lobby || !lobby.SetReady(sender, message.Ready))
            return;
        GD.Print($"[server] player {sender} is {(message.Ready ? "ready" : "not ready")}");
        _protocol.BroadcastLobby(lobby);
        TryStartMatch();
    }

    private void TryStartMatch()
    {
        if (_lobby is not { CanStart: true } lobby)
            return;

        int victoryLapTicks = (int)(MATCH_END_SECONDS * SimConfig.TICK_RATE *
            SimConfig.MATCH_END_TIME_SCALE);
        MatchSession match = new(_map.BuildMask(), _rules, Random.Shared.Next(), victoryLapTicks);
        _match = match;
        _lobby = null;
        GD.Print($"[server] all {lobby.Count} player(s) ready, starting match");
        foreach (LobbyPlayer player in lobby.Players)
            AddToMatch(player.PeerId, match);
        _protocol.BroadcastRoster(match);
    }

    private void AddToMatch(long peerId, MatchSession match)
    {
        byte team = match.AddPlayer((int)peerId);
        _protocol.SyncPlayer(peerId, match);
        GD.Print($"[server] player {peerId} joined ({match.World.Players.Count} in game)" +
                 (team != 0 ? $" on team {team}" : ""));
    }

    private void ReturnToLobby(MatchSession completedMatch)
    {
        Engine.TimeScale = 1;
        _lobby = LobbySession.For(_players.PeerIds);
        _match = null;
        GD.Print($"[server] back to lobby ({completedMatch.World.Players.Count} player(s))");
        _protocol.BroadcastLobby(_lobby);
    }

    private void OnInputsReceived(long peerId, byte[] packet)
    {
        if (_match is not { } match ||
            !InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> inputs))
            return;
        _protocol.RecordInputPayload(packet.Length);
        foreach ((int sequence, PlayerInput input) in inputs)
            match.EnqueueInput((int)peerId, sequence, input);
    }

    private void OnDebugCarve(long sender, DebugCarveMsg message)
    {
        if (_match is not { } match || message.X < 0 || message.X >= _map.Width ||
            message.Y < 0 || message.Y >= _map.Height)
            return;
        ServerExplosion? explosion = match.DebugCarve(message.X, message.Y);
        if (explosion is not { } carve)
            return;
        GD.Print($"[server] carve at ({message.X},{message.Y}) by {sender}");
        _protocol.BroadcastDebugCarve(carve);
    }

    private static MatchConfig? LoadRuleset()
    {
        string? path = CmdArgs.GetValue("--ruleset");
        if (path == null)
            return new MatchConfig();
        try
        {
            MatchConfig config = MatchConfig.FromJson(File.ReadAllText(path));
            GD.Print($"[server] ruleset '{path}' loaded");
            return config;
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[server] failed to load ruleset '{path}': {exception.Message}");
            return null;
        }
    }
}
