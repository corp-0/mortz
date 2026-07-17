using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>Translates server session state into the wire protocol. Transfer
/// ids, replication cadence, payload accounting and late-join synchronization
/// live here instead of leaking into lifecycle orchestration.</summary>
internal sealed class ServerProtocol
{
    private readonly NetworkManager _network;
    private readonly IServerLobbySettings _settings;
    private readonly PlayerDirectory _players;
    private readonly bool _printNetStats;
    private long _snapshotPayloadBytes;
    private long _mortarPayloadBytes;
    private long _inputPayloadBytes;
    private int _nextTerrainTransferId;

    public ServerProtocol(NetworkManager network, IServerLobbySettings settings,
        PlayerDirectory players,
        bool printNetStats)
    {
        _network = network;
        _settings = settings;
        _players = players;
        _printNetStats = printNetStats;
    }

    public void BroadcastLobby(LobbySession lobby)
    {
        IReadOnlyList<LobbyPlayer> players = lobby.Players;
        IReadOnlyList<(long From, long To)> offers = lobby.SwapOffers;
        new LobbyStateMsg(
            players.Select(player => player.PeerId).ToArray(),
            players.Select(player => _players.Name(player.PeerId)).ToArray(),
            players.Select(player => player.Ready ? (byte)1 : (byte)0).ToArray(),
            players.Select(player => player.Team).ToArray(),
            offers.Select(offer => offer.From).ToArray(),
            offers.Select(offer => offer.To).ToArray())
            .Broadcast();
    }

    public void BroadcastPings()
    {
        (long PeerId, int PingMs)[] pings = _network.PeerPingsMs();
        if (pings.Length == 0)
            return;
        new PingUpdateMsg(
            pings.Select(ping => ping.PeerId).ToArray(),
            pings.Select(ping => ping.PingMs).ToArray())
            .Broadcast();
    }

    public void BroadcastWins(WinTracker wins) => WinsMessage(wins).Broadcast();

    public void SendWins(long peerId, WinTracker wins) => WinsMessage(wins).SendTo(peerId);

    public void BroadcastRoster(MatchSession match)
    {
        long[] peerIds = _players.PeerIds
            .Where(peerId => match.World.Players.ContainsKey((int)peerId))
            .ToArray();
        new RosterMsg(
            peerIds,
            peerIds.Select(_players.Name).ToArray(),
            peerIds.Select(peerId => match.World.Players[(int)peerId].Skin).ToArray(),
            peerIds.Select(peerId => match.World.Players[(int)peerId].TeamId).ToArray(),
            peerIds.Select(peerId => match.World.Players[(int)peerId].NetSlot).ToArray())
            .Broadcast();
    }

    public void SyncPlayer(long peerId, MatchSession match)
    {
        SendWelcome(peerId, match);
        SendScores(peerId, match);
        SendLiveMortars(peerId, match);
        if (match.Winner is { } winner)
            new MatchEndMsg(winner.ByTeam, winner.Id).SendTo(peerId);
        if (match.FinalKill is { } finalKill)
            ToMessage(finalKill).SendTo(peerId);
    }

    public void Publish(MatchFrame frame, MatchSession match)
    {
        // The world tick is intentionally frozen during VictoryLap. Do not let
        // the repeated value trigger periodic snapshot/correction broadcasts.
        if (match.Stage == MatchStage.VICTORY_LAP && frame.MatchEnded == null)
            return;

        // Reliable ordering matters: clients arm effect suppression before the
        // matching carve/death packets arrive, then replay them cosmetically.
        if (frame.FinalKill is { } finalKill)
            ToMessage(finalKill).Broadcast();

        BroadcastMortarEvents(frame.Tick, frame.MortarEvents, match.World.Players.Count);

        foreach (ServerExplosion explosion in frame.Explosions)
        {
            GD.Print($"[server] mortar exploded at ({explosion.X},{explosion.Y})");
            BroadcastCarve(explosion);
        }
        foreach ((int firedBy, int spawnSeq) in frame.ShellRetirements)
        {
            new ShellRetireMsg(spawnSeq).SendTo(firedBy);
        }
        foreach (ServerDeath death in frame.Deaths)
        {
            GD.Print($"[server] player {death.PeerId} gibbed at " +
                     $"({(int)death.Position.X},{(int)death.Position.Y})" +
                     (death.Owned ? " (OWNED)" : ""));
            new DeathMsg(death.PeerId, PackCoordinate((int)death.Position.X),
                PackCoordinate((int)death.Position.Y)).Broadcast();
        }
        foreach (ScoredElimination elimination in frame.Eliminations)
        {
            BroadcastElimination(elimination, match.Config);
        }

        if (frame.Tick % NetConfig.TICKS_PER_SNAPSHOT == 0 && match.World.Players.Count > 0)
            BroadcastSnapshot(match);
        if (frame.Tick % NetConfig.TICKS_PER_MORTAR_CORRECTION == 0 && match.World.Mortars.Count > 0)
            BroadcastMortarCorrections(match);
        if (_printNetStats && frame.Tick % SimConfig.TICK_RATE == 0)
            PrintStats(match);

        if (frame.MatchEnded is { } winner)
        {
            string who = winner.ByTeam ? $"team {winner.Id}" : _players.Name(winner.Id);
            GD.Print($"[server] match over: {who} wins (first to {match.Config.KillTarget})");
            new MatchEndMsg(winner.ByTeam, winner.Id).Broadcast();
        }
    }

    public void RecordInputPayload(int bytes) => _inputPayloadBytes += bytes;

    public void BroadcastDebugCarve(ServerExplosion explosion) => BroadcastCarve(explosion);

    private void BroadcastMortarEvents(int tick, IReadOnlyList<SimWorld.MortarEvent> mortarEvents,
        int playerCount)
    {
        if (mortarEvents.Count == 0)
            return;
        foreach (byte[] events in MortarWire.SerializeLifecycleBatches(tick, mortarEvents))
        {
            new MortarLifecycleMsg(events).Broadcast();
            _mortarPayloadBytes += (sizeof(int) + events.Length) * playerCount;
        }
    }

    private void BroadcastSnapshot(MatchSession match)
    {
        Snapshot snapshot = match.World.TakeSnapshot(includeMortars: false);
        _snapshotPayloadBytes += _network.BroadcastSnapshot(
            peerId => snapshot.SerializeFor((int)peerId),
            peerId => match.World.Players.TryGetValue((int)peerId, out PlayerState player)
                ? player.LastInputSeq
                : -1);
    }

    private void BroadcastMortarCorrections(MatchSession match)
    {
        byte[] states = MortarWire.SerializeCorrections(match.World.Mortars);
        new MortarCorrectionMsg(match.World.Tick, states).Broadcast();
        _mortarPayloadBytes += (sizeof(int) + sizeof(int) + states.Length) * match.World.Players.Count;
    }

    private void BroadcastElimination(ScoredElimination elimination, MatchConfig config)
    {
        Scoreboard.DeathResult score = elimination.Score;
        EliminationFlags flags = score.Kind switch
        {
            Scoreboard.DeathKind.FALL => EliminationFlags.SUICIDE | EliminationFlags.FALL,
            Scoreboard.DeathKind.SUICIDE => EliminationFlags.SUICIDE,
            Scoreboard.DeathKind.TEAM_KILL => EliminationFlags.TEAM_KILL,
            _ => EliminationFlags.NONE,
        };
        if (elimination.Owned)
            flags |= EliminationFlags.OWNED;
        if (elimination.FirstBlood)
            flags |= EliminationFlags.FIRST_BLOOD;

        bool suicide = score.Kind is Scoreboard.DeathKind.FALL or Scoreboard.DeathKind.SUICIDE;
        int killerKills = suicide ? score.Victim.Kills : score.Killer?.Kills ?? 0;
        new EliminationMsg(score.KillerId, score.VictimId, flags, killerKills,
            score.Victim.Deaths, score.Team1Kills, score.Team2Kills).Broadcast();

        string teams = config.Teams ? $", teams {score.Team1Kills}-{score.Team2Kills}" : "";
        GD.Print(suicide
            ? $"[server] {_players.Name(score.VictimId)} suicides " +
              $"({score.Victim.Kills} kills, {score.Victim.Deaths} deaths{teams})"
            : $"[server] {_players.Name(score.KillerId)} killed {_players.Name(score.VictimId)} " +
              $"({killerKills} kills{teams})");
    }

    private void SendWelcome(long peerId, MatchSession match)
    {
        MapPackage map = _settings.Map;
        TerrainSyncPayload terrain = match.TerrainHistory.Build(match.World.Terrain);
        if (terrain.Data.Length > NetConfig.MAX_TERRAIN_SYNC_BYTES)
            throw new InvalidDataException($"Terrain sync is too large: {terrain.Data.Length} bytes.");
        int chunkCount = Math.Max(1,
            (terrain.Data.Length + NetConfig.TERRAIN_CHUNK_BYTES - 1) / NetConfig.TERRAIN_CHUNK_BYTES);
        int transferId = ++_nextTerrainTransferId;
        new WelcomeMsg(map.MapId, map.Hash, match.Config.ToBytes(), (byte)terrain.Encoding,
            transferId, terrain.Data.Length, checked((short)chunkCount)).SendTo(peerId);
        for (int i = 0; i < chunkCount; i++)
        {
            int offset = i * NetConfig.TERRAIN_CHUNK_BYTES;
            int length = Math.Min(NetConfig.TERRAIN_CHUNK_BYTES, terrain.Data.Length - offset);
            byte[] chunk = terrain.Data.AsSpan(offset, Math.Max(0, length)).ToArray();
            new TerrainChunkMsg(transferId, (short)i, (short)chunkCount, chunk).SendTo(peerId);
        }
        GD.Print($"[server] terrain sync to {peerId}: {terrain.Encoding}, " +
                 $"{terrain.Data.Length} B in {chunkCount} chunk(s), " +
                 $"{match.TerrainHistory.CarveCount} carve(s)");
    }

    private static void SendScores(long peerId, MatchSession match)
    {
        Scoreboard scores = match.Scores;
        new ScoreSyncMsg(
            scores.Rows.Keys.Select(id => (long)id).ToArray(),
            scores.Rows.Values.Select(row => row.Kills).ToArray(),
            scores.Rows.Values.Select(row => row.Deaths).ToArray(),
            scores.TeamKills(1),
            scores.TeamKills(2)).SendTo(peerId);
    }

    private void SendLiveMortars(long peerId, MatchSession match)
    {
        if (match.World.Mortars.Count == 0)
            return;
        SimWorld.MortarEvent[] spawns = match.World.Mortars
            .Select(mortar => new SimWorld.MortarEvent(SimWorld.MortarEventKind.SPAWN, mortar))
            .ToArray();
        foreach (byte[] events in MortarWire.SerializeLifecycleBatches(match.World.Tick, spawns))
        {
            new MortarLifecycleMsg(events).SendTo(peerId);
            _mortarPayloadBytes += sizeof(int) + events.Length;
        }
    }

    private SessionWinsMsg WinsMessage(WinTracker wins)
    {
        long[] peerIds = _players.PeerIds.ToArray();
        return new SessionWinsMsg(peerIds, peerIds.Select(wins.Wins).ToArray());
    }

    private static void BroadcastCarve(ServerExplosion explosion) =>
        new CarveMsg(PackCoordinate(explosion.X), PackCoordinate(explosion.Y),
            PackRadius(explosion.Radius), explosion.OwnerId, explosion.SpawnSeq).Broadcast();

    private void PrintStats(MatchSession match)
    {
        (double sent, double recv, double sentPackets, double recvPackets) = _network.PopWireStats();
        string peers = string.Join(" ", match.World.Players.Keys.Select(peerId =>
            $"peer={peerId} pending={match.World.PendingInputs(peerId)} " +
            $"ack={match.World.Players[peerId].LastInputSeq}"));
        GD.Print($"[stats] unix={Time.GetUnixTimeFromSystem():F3} tick={match.World.Tick} " +
                 $"sent={sent:F0}B/{sentPackets:F0}pk recv={recv:F0}B/{recvPackets:F0}pk " +
                 $"snap_app={_snapshotPayloadBytes}B mortar_app={_mortarPayloadBytes}B " +
                 $"input_app={_inputPayloadBytes}B {peers}");
        _snapshotPayloadBytes = 0;
        _mortarPayloadBytes = 0;
        _inputPayloadBytes = 0;
    }

    private static short PackCoordinate(int value) =>
        (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private static byte PackRadius(int value) =>
        (byte)Math.Clamp(value, 0, byte.MaxValue);

    private static FinalKillMsg ToMessage(FinalKillEvent finalKill)
    {
        Scoreboard.DeathKind kind = finalKill.Elimination.Score.Kind;
        FinalKillFlags flags = kind switch
        {
            Scoreboard.DeathKind.FALL => FinalKillFlags.FALL,
            Scoreboard.DeathKind.SUICIDE => FinalKillFlags.SUICIDE,
            Scoreboard.DeathKind.TEAM_KILL => FinalKillFlags.TEAM_KILL,
            _ => FinalKillFlags.NONE,
        };
        if (finalKill.Elimination.Owned)
            flags |= FinalKillFlags.OWNED;

        ServerDeath death = finalKill.Death;
        ServerExplosion? explosion = finalKill.Explosion;
        if (explosion != null)
            flags |= FinalKillFlags.EXPLOSION;
        int impactX = explosion?.X ?? (int)death.Position.X;
        int impactY = explosion?.Y ?? (int)death.Position.Y;
        return new FinalKillMsg(
            finalKill.Tick,
            finalKill.Elimination.Score.KillerId,
            finalKill.Elimination.Score.VictimId,
            flags,
            PackCoordinate((int)death.Position.X),
            PackCoordinate((int)death.Position.Y),
            PackCoordinate(impactX),
            PackCoordinate(impactY),
            PackRadius(explosion?.Radius ?? 0));
    }
}
