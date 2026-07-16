using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core;
using Mortz.Core.Input;
using Mortz.Core.Net.Messages;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Read-only session information exposed to sibling server features.</summary>
public interface IServerSession
{
    bool IsLobby { get; }
    bool ContainsPlayer(long peerId);
    string PlayerName(long peerId);
}

/// <summary>Owns lobby/match state, simulation, and the gameplay wire protocol.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerSessionController : Node, IServerSession
{
    private const float MATCH_END_SECONDS = 7;

    private readonly PlayerDirectory _players = new();
    private LobbySession? _lobby = new();
    private MatchSession? _match;
    private ServerProtocol _protocol = null!;
    private bool _debugCarveEnabled;
    private bool _subscribed;

    [Dependency]
    public IServerLobbySettings LobbySettings => this.DependOn<IServerLobbySettings>();

    public bool IsLobby => _lobby != null;
    public bool ContainsPlayer(long peerId) => _players.Contains(peerId);
    public string PlayerName(long peerId) => _players.Name(peerId);

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        NetworkManager network = NetworkManager.Instance;
        _protocol = new ServerProtocol(network, LobbySettings, _players,
            CmdArgs.HasFlag("--net-stats"));
        Subscribe(network);
    }

    public void OnExitTree()
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
        _protocol.Publish(frame, match);
        if (frame.ReturnToLobby)
            ReturnToLobby(match);
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
        LobbySettings.SendTo(peerId);
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

        int victoryLapTicks = (int)(MATCH_END_SECONDS * SimConfig.TICK_RATE);
        MapPackage selectedMap = LobbySettings.Map;
        MatchSession match = new(selectedMap.BuildMask(), LobbySettings.Rules, Random.Shared.Next(),
            victoryLapTicks, selectedMap.SpawnPoints);
        _match = match;
        _lobby = null;
        GD.Print($"[server] all {lobby.Count} player(s) ready, starting match");
        foreach (LobbyPlayer player in lobby.Players)
            AddToMatch(player.PeerId, match);
        _protocol.BroadcastRoster(match);
    }

    private void AddToMatch(long peerId, MatchSession match)
    {
        MapPackage map = LobbySettings.Map;
        if (map.SpawnPoints.Length > 0 && match.World.Players.Count >= map.SpawnPoints.Length)
            GD.PushWarning($"[server] map '{map.MapId}' only has {map.SpawnPoints.Length} spawn " +
                           $"point(s) for {match.World.Players.Count + 1} players, so some will share one");
        byte team = match.AddPlayer((int)peerId);
        _protocol.SyncPlayer(peerId, match);
        GD.Print($"[server] player {peerId} joined ({match.World.Players.Count} in game)" +
                 (team != 0 ? $" on team {team}" : ""));
    }

    private void ReturnToLobby(MatchSession completedMatch)
    {
        _lobby = LobbySession.For(_players.PeerIds);
        _match = null;
        GD.Print($"[server] back to lobby ({completedMatch.World.Players.Count} player(s))");
        _protocol.BroadcastLobby(_lobby);
        LobbySettings.Broadcast();
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
        MapPackage map = LobbySettings.Map;
        if (_match is not { } match || message.X < 0 || message.X >= map.Width ||
            message.Y < 0 || message.Y >= map.Height)
            return;
        ServerExplosion? explosion = match.DebugCarve(message.X, message.Y);
        if (explosion is not { } carve)
            return;
        GD.Print($"[server] carve at ({message.X},{message.Y}) by {sender}");
        _protocol.BroadcastDebugCarve(carve);
    }
}
