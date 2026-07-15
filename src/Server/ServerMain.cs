using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>
/// Dedicated server: owns the authoritative SimWorld, steps it at the fixed
/// physics tick (60 Hz) and broadcasts snapshots. Terrain changes go out as
/// carve events. Runs headless; never instantiates any client/UI code.
/// Peers first gather in a pre-match lobby; the match starts once everyone
/// there is ready. Anyone connecting after that drops straight into the game.
/// </summary>
public partial class ServerMain : Node
{
    private enum Phase
    {
        LOBBY,
        IN_GAME,
        /// <summary>Winner declared: the sim keeps running as a victory lap,
        /// nothing scores, and the countdown sends everyone back to the lobby.</summary>
        MATCH_END
    }

    /// <summary>Victory lap length between the winning kill and the lobby.</summary>
    private const float MATCH_END_SECONDS = 6;

    /// <summary>Map to load when --map is not passed.</summary>
    [Export] private string _defaultMap = "castlewars";

    private static readonly bool _netStats = CmdArgs.HasFlag("--net-stats");

    private SimWorld _sim = null!;
    private MapPackage _map = null!;
    private Scoreboard _scores = null!;
    private Phase _phase = Phase.LOBBY;
    private int _matchEndTicks;
    private Scoreboard.MatchWinner _winner;
    private readonly Dictionary<long, bool> _lobbyReady = new();
    private readonly Dictionary<long, string> _names = new();
    private bool _debugCarveEnabled;
    private long _snapshotPayloadBytes;
    private long _mortarPayloadBytes;
    private long _inputPayloadBytes;
    private readonly List<TerrainCarve> _carveHistory = new();
    private bool _carveLogComplete = true;
    private int _nextTerrainTransferId;

    /// <summary>Grants live lobby control via /admin
    ///  Empty = no admin access. Never logged.</summary>
    private string _adminPassword = "";

    private readonly FirstBloodTracker _firstBlood = new();

    public override void _Ready()
    {
        string mapId = CmdArgs.GetValue("--map") ?? _defaultMap;
        MapPackage? map = MapPackage.Load(mapId);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            GetTree().Quit(1);
            return;
        }
        _map = map;
        MatchConfig? config = LoadRuleset();
        ServerConfig? serverConfig = ServerConfig.Load();
        if (config == null || serverConfig == null)
        {
            GetTree().Quit(1);
            return;
        }
        _adminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (_adminPassword.Length > 0)
            GD.Print("[server] admin password set");
        _sim = new SimWorld(map.BuildMask(), config, Random.Shared.Next());

        NetworkManager net = NetworkManager.Instance;
        net.PeerJoined += OnPeerJoined;
        net.PeerLeft += OnPeerLeft;
        net.InputsReceived += OnInputsReceived;
        SetReadyMsg.Received += OnSetReady;
        _debugCarveEnabled = CmdArgs.HasFlag("--enable-debug-carve");
        if (_debugCarveEnabled)
        {
            DebugCarveMsg.Received += OnDebugCarve;
            GD.Print("[server] debug carve enabled");
        }

        int port = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        Error err = net.StartServer(port);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[server] failed to listen on port {port}: {err}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[server] listening on port {port} (protocol v{NetConfig.PROTOCOL_VERSION}, " +
                 $"map '{map.DisplayName}' {map.Width}x{map.Height}, tick {SimConfig.TICK_RATE} Hz)");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_phase == Phase.LOBBY)
            return;
        _sim.Step();
        BroadcastMortarEvents();
        foreach ((int x, int y, int radius, int owner, int spawnSeq) in _sim.Explosions)
        {
            GD.Print($"[server] mortar exploded at ({x},{y})");
            RecordCarve(x, y, radius);
            new CarveMsg(PackCoordinate(x), PackCoordinate(y), PackRadius(radius), owner, spawnSeq).Broadcast();
        }
        foreach ((int firedBy, int spawnSeq) in _sim.ShellRetirements)
            new ShellRetireMsg(spawnSeq).SendTo(firedBy);
        foreach ((int peerId, Vec2 pos, int killerId, bool owned) in _sim.Deaths)
        {
            GD.Print($"[server] player {peerId} gibbed at ({(int)pos.X},{(int)pos.Y})" +
                     (owned ? " (OWNED)" : ""));
            new DeathMsg(peerId, PackCoordinate((int)pos.X), PackCoordinate((int)pos.Y)).Broadcast();
            ScoreDeath(peerId, killerId, owned);
        }
        if (_sim.Tick % NetConfig.TICKS_PER_SNAPSHOT == 0 && _sim.Players.Count > 0)
        {
            Snapshot snapshot = _sim.TakeSnapshot(includeMortars: false);
            _snapshotPayloadBytes += NetworkManager.Instance.BroadcastSnapshot(
                peerId => snapshot.SerializeFor((int)peerId),
                peerId => _sim.Players.TryGetValue((int)peerId, out PlayerState p) ? p.LastInputSeq : -1);
        }
        if (_sim.Tick % NetConfig.TICKS_PER_MORTAR_CORRECTION == 0 && _sim.Mortars.Count > 0)
        {
            byte[] states = MortarWire.SerializeCorrections(_sim.Mortars);
            new MortarCorrectionMsg(_sim.Tick, states).Broadcast();
            _mortarPayloadBytes += (sizeof(int) + sizeof(int) + states.Length) * _sim.Players.Count;
        }
        if (_netStats && _sim.Tick % SimConfig.TICK_RATE == 0)
            PrintNetStats();
        if (_phase == Phase.MATCH_END && --_matchEndTicks == 0)
            ReturnToLobby();
    }

    private void BroadcastMortarEvents()
    {
        if (_sim.MortarEvents.Count == 0)
            return;
        foreach (byte[] events in MortarWire.SerializeLifecycleBatches(_sim.Tick, _sim.MortarEvents))
        {
            new MortarLifecycleMsg(events).Broadcast();
            _mortarPayloadBytes += (sizeof(int) + events.Length) * _sim.Players.Count;
        }
    }

    /// <summary>Fresh world on the same map and rules; everyone back to ready-up.</summary>
    private void ReturnToLobby()
    {
        Engine.TimeScale = 1;
        GD.Print($"[server] back to lobby ({_sim.Players.Count} player(s))");
        foreach (int peerId in _sim.Players.Keys)
            _lobbyReady[peerId] = false;
        _sim = new SimWorld(_map.BuildMask(), _sim.Config, Random.Shared.Next());
        _carveHistory.Clear();
        _carveLogComplete = true;
        _phase = Phase.LOBBY;
        BroadcastLobbyState();
    }

    // One line per second: wire totals from ENet plus each player's input
    // backlog. pending > 1 means standing input latency, one tick per input.
    private void PrintNetStats()
    {
        (double sent, double recv, double sentPk, double recvPk) = NetworkManager.Instance.PopWireStats();
        string peers = string.Join(" ", _sim.Players.Keys.Select(
            id => $"peer={id} pending={_sim.PendingInputs(id)} ack={_sim.Players[id].LastInputSeq}"));
        GD.Print($"[stats] unix={Time.GetUnixTimeFromSystem():F3} tick={_sim.Tick} " +
                 $"sent={sent:F0}B/{sentPk:F0}pk recv={recv:F0}B/{recvPk:F0}pk " +
                 $"snap_app={_snapshotPayloadBytes}B mortar_app={_mortarPayloadBytes}B " +
                 $"input_app={_inputPayloadBytes}B {peers}");
        _snapshotPayloadBytes = 0;
        _mortarPayloadBytes = 0;
        _inputPayloadBytes = 0;
    }

    private void OnPeerJoined(long peerId, string playerName)
    {
        playerName = PlayerNameSanitizer.Sanitize(playerName);
        if (playerName.Length == 0)
            playerName = $"Player {peerId}";
        _names[peerId] = playerName;

        if (_phase != Phase.LOBBY)
        {
            AddToGame(peerId);
            BroadcastRoster(); // after Welcome, so the joiner's GameView is listening
            return;
        }
        _lobbyReady[peerId] = false;
        GD.Print($"[server] player {peerId} '{playerName}' entered lobby ({_lobbyReady.Count} waiting)");
        BroadcastLobbyState();
    }

    private void OnPeerLeft(long peerId)
    {
        _names.Remove(peerId);
        if (_lobbyReady.Remove(peerId))
        {
            GD.Print($"[server] player {peerId} left lobby ({_lobbyReady.Count} waiting)");
            BroadcastLobbyState();
            TryStartMatch(); // the leaver may have been the only one not ready
            return;
        }
        _sim.RemovePlayer((int)peerId);
        _scores.RemovePlayer((int)peerId); // their kills stay on the team total
        GD.Print($"[server] player {peerId} left ({_sim.Players.Count} in game)");
        BroadcastRoster();
    }

    private void OnSetReady(long sender, SetReadyMsg msg)
    {
        if (!_lobbyReady.ContainsKey(sender))
            return;
        _lobbyReady[sender] = msg.Ready;
        GD.Print($"[server] player {sender} is {(msg.Ready ? "ready" : "not ready")}");
        BroadcastLobbyState();
        TryStartMatch();
    }

    private void BroadcastLobbyState()
    {
        long[] peers = _lobbyReady.Keys.ToArray();
        string[] names = peers.Select(p => _names[p]).ToArray();
        byte[] flags = peers.Select(p => _lobbyReady[p] ? (byte)1 : (byte)0).ToArray();
        new LobbyStateMsg(peers, names, flags).Broadcast();
    }

    private void TryStartMatch()
    {
        if (_phase != Phase.LOBBY || _lobbyReady.Count == 0 || _lobbyReady.ContainsValue(false))
            return;
        _phase = Phase.IN_GAME;
        _scores = new Scoreboard(_sim.Config);
        _firstBlood.Reset();
        GD.Print($"[server] all {_lobbyReady.Count} player(s) ready, starting match");
        foreach (long peerId in _lobbyReady.Keys)
            AddToGame(peerId);
        _lobbyReady.Clear();
        BroadcastRoster(); // lobby-phase rosters would predate everyone's GameView
    }

    /// <summary>Name list for in-game display (nameplates); only sent while IN_GAME,
    /// the lobby gets names through the lobby state instead.</summary>
    private void BroadcastRoster()
    {
        long[] peers = _names.Keys.Where(p => _sim.Players.ContainsKey((int)p)).ToArray();
        string[] names = peers.Select(p => _names[p]).ToArray();
        byte[] skins = peers.Select(p => _sim.Players[(int)p].Skin).ToArray();
        byte[] teams = peers.Select(p => _sim.Players[(int)p].TeamId).ToArray();
        byte[] slots = peers.Select(p => _sim.Players[(int)p].NetSlot).ToArray();
        new RosterMsg(peers, names, skins, teams, slots).Broadcast();
    }

    /// <summary>The host's ruleset preset (--ruleset path.json), or defaults
    /// without the flag. Null means the file was asked for but unusable; the
    /// host should hear about that, not silently run defaults.</summary>
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
        catch (Exception e)
        {
            GD.PrintErr($"[server] failed to load ruleset '{path}': {e.Message}");
            return null;
        }
    }

    private void AddToGame(long peerId)
    {
        byte team = NextTeam();
        _sim.AddPlayer((int)peerId, team);
        _scores.AddPlayer((int)peerId, team);
        SendWelcome(peerId);
        SendLiveMortars(peerId);
        if (_phase == Phase.MATCH_END)
            new MatchEndMsg(_winner.ByTeam, _winner.Id).SendTo(peerId); // sync their slow motion and banner
        GD.Print($"[server] player {peerId} joined ({_sim.Players.Count} in game)" +
                 (team != 0 ? $" on team {team}" : ""));
    }

    private void SendLiveMortars(long peerId)
    {
        if (_sim.Mortars.Count == 0)
            return;
        SimWorld.MortarEvent[] spawns = _sim.Mortars
            .Select(m => new SimWorld.MortarEvent(SimWorld.MortarEventKind.Spawn, m))
            .ToArray();
        foreach (byte[] events in MortarWire.SerializeLifecycleBatches(_sim.Tick, spawns))
        {
            new MortarLifecycleMsg(events).SendTo(peerId);
            _mortarPayloadBytes += sizeof(int) + events.Length;
        }
    }

    private void SendWelcome(long peerId)
    {
        TerrainSyncPayload terrain = _carveLogComplete
            ? TerrainSync.Build(_sim.Terrain, _carveHistory)
            : new TerrainSyncPayload(TerrainSyncEncoding.RemovedBitmap, _sim.Terrain.SerializeRemoved());
        if (terrain.Data.Length > NetConfig.MAX_TERRAIN_SYNC_BYTES)
            throw new InvalidDataException($"Terrain sync is too large: {terrain.Data.Length} bytes.");
        int count = Math.Max(1, (terrain.Data.Length + NetConfig.TERRAIN_CHUNK_BYTES - 1) /
            NetConfig.TERRAIN_CHUNK_BYTES);
        int transferId = ++_nextTerrainTransferId;
        new WelcomeMsg(_map.MapId, _map.Hash, _sim.Config.ToBytes(), (byte)terrain.Encoding,
            transferId, terrain.Data.Length, checked((short)count)).SendTo(peerId);
        for (int i = 0; i < count; i++)
        {
            int offset = i * NetConfig.TERRAIN_CHUNK_BYTES;
            int length = Math.Min(NetConfig.TERRAIN_CHUNK_BYTES, terrain.Data.Length - offset);
            byte[] chunk = terrain.Data.AsSpan(offset, Math.Max(0, length)).ToArray();
            new TerrainChunkMsg(transferId, (short)i, (short)count, chunk).SendTo(peerId);
        }
        GD.Print($"[server] terrain sync to {peerId}: {terrain.Encoding}, " +
                 $"{terrain.Data.Length} B in {count} chunk(s), {_carveHistory.Count} carve(s)");
    }

    private void RecordCarve(int x, int y, int radius)
    {
        if (_carveHistory.Count >= TerrainSync.MAX_CARVES)
        {
            _carveLogComplete = false;
            return; // bitmap remains exact once the log stops growing
        }
        _carveHistory.Add(new TerrainCarve(
            (short)Math.Clamp(x, short.MinValue, short.MaxValue),
            (short)Math.Clamp(y, short.MinValue, short.MaxValue),
            (byte)Math.Clamp(radius, 0, byte.MaxValue)));
    }

    /// <summary>Balanced fill: each joiner (match start iterates joins too)
    /// lands on the smaller team. Proper lobby assignment with admin control
    /// replaces this eventually.</summary>
    private byte NextTeam()
    {
        if (!_sim.Config.Teams)
            return 0;
        int one = _sim.Players.Values.Count(p => p.TeamId == 1);
        int two = _sim.Players.Values.Count(p => p.TeamId == 2);
        return (byte)(one <= two ? 1 : 2);
    }

    private void ScoreDeath(int victimId, int killerId, bool owned)
    {
        if (_phase != Phase.IN_GAME)
            return; // victory lap: the board is closed

        EliminationFlags flags = EliminationFlags.NONE;
        if (killerId == 0)
            flags |= EliminationFlags.SUICIDE | EliminationFlags.FALL;
        else if (killerId == victimId)
            flags |= EliminationFlags.SUICIDE;
        else if (_sim.Config.Teams &&
                 _scores.Rows.TryGetValue(victimId, out Scoreboard.Row victimBefore) &&
                 _scores.Rows.TryGetValue(killerId, out Scoreboard.Row killerBefore) &&
                 victimBefore.TeamId != 0 && victimBefore.TeamId == killerBefore.TeamId)
            flags |= EliminationFlags.TEAM_KILL;
        if (owned)
            flags |= EliminationFlags.OWNED;

        Scoreboard.MatchWinner? winner = _scores.RecordDeath(victimId, killerId);
        if (!_scores.Rows.TryGetValue(victimId, out Scoreboard.Row victim))
            return; // victim unknown to the board, nothing scored

        bool suicide = (flags & EliminationFlags.SUICIDE) != 0;
        bool teamKill = (flags & EliminationFlags.TEAM_KILL) != 0;
        bool killerKnown = _scores.Rows.TryGetValue(killerId, out Scoreboard.Row killer);
        if (_firstBlood.TryClaim(killerKnown, suicide, teamKill))
            flags |= EliminationFlags.FIRST_BLOOD;
        int killerKills = suicide ? victim.Kills : killer.Kills;
        new EliminationMsg(killerId, victimId, flags, killerKills, victim.Deaths,
            _scores.TeamKills(1), _scores.TeamKills(2)).Broadcast();

        string teams = _sim.Config.Teams ? $", teams {_scores.TeamKills(1)}-{_scores.TeamKills(2)}" : "";
        GD.Print(suicide
            ? $"[server] {Name(victimId)} suicides ({victim.Kills} kills, {victim.Deaths} deaths{teams})"
            : $"[server] {Name(killerId)} killed {Name(victimId)} ({killerKills} kills{teams})");
        if (winner is { } w)
            EndMatch(w);
    }

    private void EndMatch(Scoreboard.MatchWinner winner)
    {
        _phase = Phase.MATCH_END;
        _winner = winner;
        // Victory lap in slow motion: fewer sim ticks so it still lasts
        // MATCH_END_SECONDS of real time.
        Engine.TimeScale = SimConfig.MATCH_END_TIME_SCALE;
        _matchEndTicks = (int)(MATCH_END_SECONDS * SimConfig.TICK_RATE * SimConfig.MATCH_END_TIME_SCALE);
        string who = winner.ByTeam ? $"team {winner.Id}" : Name(winner.Id);
        GD.Print($"[server] match over: {who} wins (first to {_sim.Config.KillTarget})");
        new MatchEndMsg(winner.ByTeam, winner.Id).Broadcast();
    }

    private new string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : peerId.ToString();

    private void OnInputsReceived(long peerId, byte[] packet)
    {
        if (!InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> inputs))
            return;
        _inputPayloadBytes += packet.Length;
        foreach ((int seq, PlayerInput input) in inputs)
            _sim.EnqueueInput((int)peerId, seq, input);
    }

    private void OnDebugCarve(long sender, DebugCarveMsg msg)
    {
        if (msg.X < 0 || msg.X >= _map.Width || msg.Y < 0 || msg.Y >= _map.Height)
            return;
        List<(int X, int Y)> removed = _sim.Terrain.CarveCircle(msg.X, msg.Y, SimConfig.DEBUG_CARVE_RADIUS);
        GD.Print($"[server] carve at ({msg.X},{msg.Y}) by {sender}: {removed.Count} px");
        if (removed.Count > 0)
        {
            RecordCarve(msg.X, msg.Y, SimConfig.DEBUG_CARVE_RADIUS);
            new CarveMsg(PackCoordinate(msg.X), PackCoordinate(msg.Y), SimConfig.DEBUG_CARVE_RADIUS, 0, -1).Broadcast();
        }
    }

    private static short PackCoordinate(int value) =>
        (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private static byte PackRadius(int value) =>
        (byte)Math.Clamp(value, 0, byte.MaxValue);
}
