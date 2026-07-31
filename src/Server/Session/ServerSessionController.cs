using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Admin;
using Mortz.Core.Chat;
using Mortz.Core.Input;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Server.Chat;
using Mortz.Server.Lobby;
using Mortz.Server.Match;
using Mortz.Shared;

namespace Mortz.Server.Session;

/// <summary>Read-only session information exposed to sibling server features.</summary>
public interface IServerSession
{
    bool IsLobby { get; }
    int PlayerCount { get; }
    bool ContainsPlayer(int peerId);
    string PlayerName(int peerId);

    /// <summary>Raised while the name is still known, before the directory drops it.</summary>
    event Action<int, string>? PlayerLeft;
}

/// <summary>Owns lobby/match state, simulation, and the gameplay wire protocol.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerSessionController : Node, IServerSession
{
    private const float MATCH_END_SECONDS = 7;
    private const double PING_INTERVAL_SECONDS = 1;

    private readonly PlayerDirectory _players = new();
    private readonly WinTracker _wins = new();
    private double _pingCountdown;
    private LobbySession? _lobby = new();
    private MatchSession? _match;
    private ServerProtocol _protocol = null!;
    private bool _subscribed;

    [Dependency]
    public IServerLobbySettings LobbySettings => DependentExtensions.DependOn<IServerLobbySettings>(this);

    [Dependency]
    private NetworkManager Network => DependentExtensions.DependOn<NetworkManager>(this);

    [Dependency]
    private IServerAdminAuthorizer Admin => DependentExtensions.DependOn<IServerAdminAuthorizer>(this);

    public bool IsLobby => _lobby != null;
    public int PlayerCount => _players.Count;
    public bool ContainsPlayer(int peerId) => _players.Contains(peerId);
    public string PlayerName(int peerId) => _players.Name(peerId);

    public event Action<int, string>? PlayerLeft;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _protocol = new ServerProtocol(Network, LobbySettings, _players,
            CmdArgs.HasFlag("--net-stats"));
        Subscribe(Network);
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Network.PeerJoined -= OnPeerJoined;
        Network.PeerLeft -= OnPeerLeft;
        Network.InputsReceived -= OnInputsReceived;
        SetReadyMsg.Received -= OnSetReady;
        TeamJoinRequestMsg.Received -= OnTeamJoinRequest;
        TeamSwapRequestMsg.Received -= OnTeamSwapRequest;
        EndMatchRequestMsg.Received -= OnEndMatchRequest;
        LobbySettings.Changed -= OnConfigChanged;
        _subscribed = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        _pingCountdown -= delta;
        if (_pingCountdown <= 0)
        {
            _pingCountdown = PING_INTERVAL_SECONDS;
            _protocol.BroadcastPings();
        }

        if (_match is not MatchSession match)
            return;

        MatchFrame frame = match.Step();
        _protocol.Publish(frame, match);
        if (frame.MatchEnded is Victor winner)
            RecordWins(winner, match);
        if (frame.ReturnToLobby)
            ReturnToLobby(match);
    }

    private void Subscribe(NetworkManager network)
    {
        network.PeerJoined += OnPeerJoined;
        network.PeerLeft += OnPeerLeft;
        network.InputsReceived += OnInputsReceived;
        SetReadyMsg.Received += OnSetReady;
        TeamJoinRequestMsg.Received += OnTeamJoinRequest;
        TeamSwapRequestMsg.Received += OnTeamSwapRequest;
        EndMatchRequestMsg.Received += OnEndMatchRequest;
        LobbySettings.Changed += OnConfigChanged;
        _subscribed = true;
    }

    private void OnPeerJoined(int peerId, string requestedName)
    {
        string name = _players.Add(peerId, requestedName);
        _protocol.SendWins(peerId, _wins);
        if (_match is MatchSession match)
        {
            AddToMatch(peerId, match);
            _protocol.BroadcastRoster(match);
            return;
        }

        LobbySession lobby = _lobby!;
        lobby.SetTeamsEnabled(LobbySettings.Config.Rules.Teams);
        lobby.Add(peerId, name);
        GD.Print($"[server] player {peerId} '{name}' entered lobby ({lobby.Count} waiting)");
        _protocol.BroadcastLobby(lobby);
        LobbySettings.SendTo(peerId);
    }

    private void OnPeerLeft(int peerId)
    {
        PlayerLeft?.Invoke(peerId, _players.Name(peerId));
        _players.Remove(peerId);
        _wins.Remove(peerId);
        if (_match is MatchSession match)
        {
            match.RemovePlayer(peerId);
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

    private void OnTeamJoinRequest(int sender, TeamJoinRequestMsg message)
    {
        if (_lobby is not LobbySession lobby || TeamWire.FromByte(message.Team) is not Team team ||
            !lobby.TrySetTeam(sender, team))
            return;
        GD.Print($"[server] player {sender} moved to {Teams.Name(team)}");
        _protocol.BroadcastLobby(lobby);
    }

    private void OnTeamSwapRequest(int sender, TeamSwapRequestMsg message)
    {
        if (_lobby is not LobbySession lobby)
            return;
        SwapResult result = lobby.RequestSwap(sender, message.TargetPeerId);
        if (result == SwapResult.NONE)
            return;
        GD.Print(result switch
        {
            SwapResult.OFFERED =>
                $"[server] player {sender} offers a team swap to {message.TargetPeerId}",
            SwapResult.CANCELLED => $"[server] player {sender} cancelled their swap offer",
            _ => $"[server] players {sender} and {message.TargetPeerId} swapped teams",
        });
        _protocol.BroadcastLobby(lobby);
    }

    private void OnConfigChanged(LobbySettingsChange change)
    {
        if (!change.AffectsConfig) return;
        if (_lobby is not LobbySession lobby || !lobby.SetTeamsEnabled(LobbySettings.Config.Rules.Teams))
            return;
        GD.Print($"[server] lobby teams {(LobbySettings.Config.Rules.Teams ? "assigned" : "cleared")}");
        _protocol.BroadcastLobby(lobby);
    }

    private void OnSetReady(int sender, SetReadyMsg message)
    {
        if (_lobby is not LobbySession lobby || !lobby.SetReady(sender, message.Ready))
            return;
        GD.Print($"[server] player {sender} is {(message.Ready ? "ready" : "not ready")}");
        _protocol.BroadcastLobby(lobby);
        TryStartMatch();
    }

    private void OnEndMatchRequest(int sender, EndMatchRequestMsg message)
    {
        if (!Admin.TryAuthorize(sender, message.Sequence, AdminAction.END_MATCH,
                [], message.Tag))
            return;
        if (_match is not MatchSession match)
        {
            ChatProtocol.SendTo(sender, new ChatLine.System("No match is running."));
            return;
        }

        GD.Print($"[server] admin {sender} '{_players.Name(sender)}' force-ended the match");
        ReturnToLobby(match);
        // After the lobby broadcast so it lands in the fresh lobby chats.
        ChatProtocol.Broadcast(
            new ChatLine.System($"{_players.Name(sender)} ended the match."));
    }

    private void TryStartMatch()
    {
        if (_lobby is not { CanStart: true } lobby)
            return;

        int victoryLapTicks = (int)(MATCH_END_SECONDS * SimConfig.TICK_RATE);
        MapPackage selectedMap = LobbySettings.Map;
        MatchSession match = new(selectedMap.BuildMask(), LobbySettings.Config, Random.Shared.Next(),
            victoryLapTicks, selectedMap.SpawnPoints);
        _match = match;
        _lobby = null;
        GD.Print($"[server] all {lobby.Count} player(s) ready, starting match");
        foreach (LobbyMember member in lobby.Players)
        {
            AddToMatch(member.PeerId, match, member.Team);
        }
        _protocol.BroadcastRoster(match);
    }

    private void AddToMatch(int peerId, MatchSession match, Team? lobbyTeam = null)
    {
        MapPackage map = LobbySettings.Map;
        if (map.SpawnPoints.Length > 0 && match.World.Players.Count >= map.SpawnPoints.Length)
            GD.PushWarning($"[server] map '{map.MapId}' only has {map.SpawnPoints.Length} spawn " +
                           $"point(s) for {match.World.Players.Count + 1} players, so some will share one");
        Team? team = match.AddPlayer(peerId, lobbyTeam);
        _protocol.SyncPlayer(peerId, match);
        string onTeam = team is Team assigned ? $" on {Teams.Name(assigned)}" : "";
        GD.Print($"[server] player {peerId} joined " +
                 $"({match.World.Players.Count} in game){onTeam}");
    }

    private void RecordWins(Victor winner, MatchSession match)
    {
        foreach (int peerId in match.WinnerPeers(winner))
        {
            _wins.RecordWin(peerId);
            GD.Print($"[server] {_players.Name(peerId)} now has " +
                     $"{_wins.Wins(peerId)} session win(s)");
        }
        _protocol.BroadcastWins(_wins);
    }

    private void ReturnToLobby(MatchSession completedMatch)
    {
        _lobby = LobbySession.For(_players.Named, LobbySettings.Config.Rules.Teams);
        _match = null;
        GD.Print($"[server] back to lobby ({completedMatch.World.Players.Count} player(s))");
        _protocol.BroadcastLobby(_lobby);
        LobbySettings.Broadcast();
    }

    private void OnInputsReceived(int peerId, byte[] packet)
    {
        if (_match is not MatchSession match ||
            !InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> inputs))
            return;
        _protocol.RecordInputPayload(packet.Length);
        foreach ((int sequence, PlayerInput input) in inputs)
        {
            match.EnqueueInput(peerId, sequence, input);
        }
    }
}
