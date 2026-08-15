using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Roster;
using Mortz.Core.Net.Score;
using Mortz.Core.Net.Sim;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Server.Content;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Serilog;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Server.Match;

/// <summary>Turns match state into the wire protocol: transfer ids, replication
/// cadence, payload accounting, and late-join sync.</summary>
public class MatchReplication(
    MatchRuntime runtime,
    Roster roster,
    MapSnapshot map,
    IServerLink link,
    ILogger log,
    bool printNetStats)
    : ISyncJip
{
    private readonly ILogger _statsLog = log.ForContext("Area", "stats");
    private long _snapshotPayloadBytes;
    private long _mortarPayloadBytes;
    private long _inputPayloadBytes;
    private int _nextTerrainTransferId;

    public void Sync(Player jipPlayer)
        => SendCurrentState(jipPlayer);

    public void Enter(Player player, int generation, bool initialPhase)
    {
        SendWelcome(player, generation);
        if (initialPhase)
            SendCurrentState(player);
    }

    private void SendCurrentState(Player jipPlayer)
    {
        int peerId = jipPlayer.PeerId;
        SendScores(peerId);
        SendLiveMortars(peerId);
        if (runtime.ActiveMatchPoint is MatchPoint matchPoint)
        {
            link.Send(peerId,
                MatchProtocol.Encode(matchPoint));
        }
        if (runtime.Winner is Victor winner)
            link.Send(peerId, MatchProtocol.Encode(winner));
        if (runtime.FinalKill is FinalKillEvent finalKill)
            link.Send(peerId, ToMessage(finalKill));
    }

    public void BroadcastRoster()
    {
        List<RosterEntry> entries = [];
        foreach (Player player in roster)
        {
            if (!runtime.World.Players.TryGetValue(player.PeerId, out PlayerState state))
                continue;
            entries.Add(new RosterEntry(player.PeerId, player.Name,
                state.Skin, state.Team, state.NetSlot));
        }
        link.Broadcast(new RosterMsg([.. entries]));
        foreach (RosterEntry entry in entries)
        {
            link.Broadcast(new PlayerModifiersMsg(entry.PeerId,
                ModifierWire.Serialize(runtime.World.Modifiers(entry.PeerId))));
        }
    }

    public void Publish(in MatchUpdate update, ServerTime time)
    {
        // Tick is frozen during VictoryLap; skip periodic snapshot/correction broadcasts.
        if (runtime.Stage == MatchStage.VICTORY_LAP && update.MatchEnded == null)
            return;

        // Send before carve/death so clients arm effect suppression first.
        if (update.FinalKill is FinalKillEvent finalKill)
            link.Broadcast(ToMessage(finalKill));

        BroadcastMortarEvents(update.Tick, update.MortarEvents, runtime.World.Players.Count);

        foreach (Explosion explosion in update.Explosions)
        {
            log.Information("mortar exploded at ({X},{Y})", explosion.X, explosion.Y);
            BroadcastCarve(explosion);
        }
        foreach (ShellRetirement retirement in update.ShellRetirements)
        {
            link.Send(retirement.FiredBy, new ShellRetireMsg(retirement.SpawnSeq));
        }
        foreach (Death death in update.Deaths)
        {
            log.Information("player {PeerId} gibbed at ({X},{Y}){Owned:l}", death.PeerId,
                (int)death.Position.X, (int)death.Position.Y, death.Owned ? " (OWNED)" : "");
            link.Broadcast(new DeathMsg(death.PeerId, PackCoordinate((int)death.Position.X),
                PackCoordinate((int)death.Position.Y)));
        }
        foreach (MatchParticipationChange participationChange in update.ParticipationChanges)
        {
            MatchParticipation state = participationChange.State;
            link.Send(participationChange.PeerId, new MatchParticipationMsg(
                state.Seat, state.Activity, state.Reason, state.ReturnTick));
        }
        foreach (ScoredKill elimination in update.Eliminations)
        {
            BroadcastElimination(elimination, runtime.Config.Rules);
        }
        foreach (Judgment judgment in update.GameEvents)
        {
            log.Information("game event {Kind} by {Actor}{Magnitude:l}", judgment.Kind,
                Name(judgment.ActorId), judgment.Magnitude > 0 ? $" x{judgment.Magnitude}" : "");
            link.Broadcast(new GameEventMsg(judgment.Kind, judgment.ActorId, judgment.VictimId,
                judgment.Magnitude, judgment.Detail));
        }
        if (update.MatchPoint is MatchPointChange change)
        {
            log.Information("match point {MatchPointState}", change.Held != null ? "on" : "off");
            link.Broadcast(MatchProtocol.Encode(change.Held));
        }

        if (update.Tick % NetConfig.TICKS_PER_SNAPSHOT == 0 && runtime.World.Players.Count > 0)
            BroadcastSnapshot();
        if (update.Tick % NetConfig.TICKS_PER_MORTAR_CORRECTION == 0 && runtime.World.Mortars.Count > 0)
            BroadcastMortarCorrections();
        if (printNetStats && update.Tick % SimConfig.TICK_RATE == 0)
            PrintStats(time);

        if (update.MatchEnded is Victor winner)
        {
            log.Information("match over: {Winner} wins", Describe(winner));
            link.Broadcast(MatchProtocol.Encode(winner));
        }
    }

    public void RecordInputPayload(int bytes) => _inputPayloadBytes += bytes;

    private void BroadcastMortarEvents(int tick, IReadOnlyList<SimWorld.MortarEvent> mortarEvents,
        int playerCount)
    {
        if (mortarEvents.Count == 0)
            return;
        foreach (byte[] events in MortarWire.SerializeLifecycleBatches(tick, mortarEvents))
        {
            link.Broadcast(new MortarLifecycleMsg(events));
            _mortarPayloadBytes += (sizeof(int) + events.Length) * playerCount;
        }
    }

    public MatchSnapshot CaptureSnapshot()
    {
        Snapshot simulation = runtime.World.TakeSnapshot(includeMortars: false);
        ReplicatedPlayer[] players =
        [
            .. simulation.Players.Select(player => new ReplicatedPlayer(
                player,
                runtime.PresentationOf(player)))
        ];
        return new MatchSnapshot(simulation.Tick, players);
    }

    private void BroadcastSnapshot()
    {
        MatchSnapshot snapshot = CaptureSnapshot();
        _snapshotPayloadBytes += link.BroadcastSnapshot(
            snapshot.SerializeFor,
            peerId => runtime.World.Players.TryGetValue(peerId, out PlayerState player)
                ? player.LastInputSeq
                : -1);
    }

    private void BroadcastMortarCorrections()
    {
        byte[] states = MortarWire.SerializeCorrections(runtime.World.Mortars);
        link.Broadcast(new MortarCorrectionMsg(runtime.World.Tick, states));
        _mortarPayloadBytes +=
            (sizeof(int) + sizeof(int) + states.Length) * runtime.World.Players.Count;
    }

    private void BroadcastElimination(ScoredKill kill, ModeRules config)
    {
        DeathScore score = kill.Score;
        EliminationFlags flags = score.Kind switch
        {
            DeathKind.FALL => EliminationFlags.SUICIDE | EliminationFlags.FALL,
            DeathKind.SUICIDE => EliminationFlags.SUICIDE,
            DeathKind.TEAM_KILL => EliminationFlags.TEAM_KILL,
            _ => EliminationFlags.NONE,
        };
        if (kill.Owned)
            flags |= EliminationFlags.OWNED;
        if (kill.FirstBlood)
            flags |= EliminationFlags.FIRST_BLOOD;

        bool suicide = score.Kind is DeathKind.FALL or DeathKind.SUICIDE;
        int killerKills = suicide ? score.Victim.Kills : score.Killer?.Kills ?? 0;
        link.Broadcast(new EliminationMsg(score.KillerId, score.VictimId, flags, killerKills,
            score.Victim.Deaths, score.Reward?.PeerId ?? 0, score.Reward?.Kills ?? 0,
            score.TeamKills.Blue, score.TeamKills.Red));

        string teams = config.Teams
            ? $", teams {score.TeamKills.Blue}-{score.TeamKills.Red}"
            : "";
        if (suicide)
        {
            log.Information("{Victim} suicides ({Kills} kills, {Deaths} deaths{Teams:l})",
                Name(score.VictimId), score.Victim.Kills, score.Victim.Deaths, teams);
        }
        else
        {
            log.Information("{Killer} killed {Victim} ({Kills} kills{Teams:l})",
                Name(score.KillerId), Name(score.VictimId), killerKills, teams);
        }
    }

    private string Describe(Victor victor) => victor switch
    {
        Victor.Team team => Teams.Name(team.Value),
        Victor.Player player => Name(player.PeerId),
        _ => throw new ArgumentOutOfRangeException(nameof(victor)),
    };

    private void SendWelcome(Player jipPlayer, int generation)
    {
        int peerId = jipPlayer.PeerId;
        TerrainSyncPayload terrain = runtime.TerrainHistory.Build(runtime.World.Terrain);
        if (terrain.Data.Length > NetConfig.MAX_TERRAIN_SYNC_BYTES)
            throw new InvalidDataException($"Terrain sync is too large: {terrain.Data.Length} bytes.");
        int chunkCount = Math.Max(1,
            (terrain.Data.Length + NetConfig.TERRAIN_CHUNK_BYTES - 1) / NetConfig.TERRAIN_CHUNK_BYTES);
        int transferId = ++_nextTerrainTransferId;
        MatchParticipation participation = runtime.ParticipationOf(jipPlayer);
        MatchSnapshot initialSnapshot = CaptureSnapshot();
        int ack = runtime.World.Players.TryGetValue(peerId, out PlayerState player)
            ? player.LastInputSeq
            : -1;
        link.Send(peerId, new MatchLoadMsg(map.MapId, map.Hash, runtime.Config.ToBytes(),
            (byte)terrain.Encoding, transferId, terrain.Data.Length, checked((short)chunkCount),
            participation.Seat, participation.Activity, participation.Reason,
            participation.ReturnTick, initialSnapshot.SerializeFor(peerId), ack, generation));
        for (int i = 0; i < chunkCount; i++)
        {
            int offset = i * NetConfig.TERRAIN_CHUNK_BYTES;
            int length = Math.Min(NetConfig.TERRAIN_CHUNK_BYTES, terrain.Data.Length - offset);
            byte[] chunk = terrain.Data.AsSpan(offset, Math.Max(0, length)).ToArray();
            link.Send(peerId, new TerrainChunkMsg(transferId, (short)i, (short)chunkCount, chunk));
        }
        log.Information(
            "terrain sync to {PeerId}: {Encoding}, {Bytes} B in {Chunks} chunk(s), {Carves} carve(s)",
            peerId, terrain.Encoding, terrain.Data.Length, chunkCount,
            runtime.TerrainHistory.CarveCount);
    }

    private void SendScores(int peerId)
    {
        ScoreRow[] rows =
        [
            .. runtime.ScoreRows()
                .Select(row => new ScoreRow(row.Player.PeerId, row.Score.Kills, row.Score.Deaths))
        ];
        link.Send(peerId, new ScoreSyncMsg(
            rows, runtime.TeamKills.Blue, runtime.TeamKills.Red));
    }

    private void SendLiveMortars(int peerId)
    {
        if (runtime.World.Mortars.Count == 0)
            return;
        SimWorld.MortarEvent[] spawns = runtime.World.Mortars
            .Select(mortar => new SimWorld.MortarEvent(SimWorld.MortarEventKind.SPAWN, mortar))
            .ToArray();
        foreach (byte[] events in MortarWire.SerializeLifecycleBatches(runtime.World.Tick, spawns))
        {
            link.Send(peerId, new MortarLifecycleMsg(events));
            _mortarPayloadBytes += sizeof(int) + events.Length;
        }
    }


    private string Name(int peerId) => roster.Find(peerId)?.Name ?? $"peer {peerId}";

    private void BroadcastCarve(Explosion explosion) =>
        link.Broadcast(new CarveMsg(PackCoordinate(explosion.X), PackCoordinate(explosion.Y),
            PackRadius(explosion.Radius), explosion.OwnerId, explosion.SpawnSeq));

    private void PrintStats(ServerTime time)
    {
        WireStats wire = link.PopWireStats();
        string peers = string.Join(" ", runtime.World.Players.Keys.Select(peerId =>
            $"peer={peerId} pending={runtime.World.PendingInputs(peerId)} " +
            $"ack={runtime.World.Players[peerId].LastInputSeq}"));
        _statsLog.Information(
            "ms={Ms} tick={Tick} sent={SentBytes:F0}B/{SentPackets:F0}pk " +
            "recv={RecvBytes:F0}B/{RecvPackets:F0}pk snap_app={SnapshotBytes}B " +
            "mortar_app={MortarBytes}B input_app={InputBytes}B {Peers:l}",
            time.Ms, runtime.World.Tick, wire.SentBytes, wire.SentPackets, wire.RecvBytes,
            wire.RecvPackets, _snapshotPayloadBytes, _mortarPayloadBytes, _inputPayloadBytes,
            peers);
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
        DeathKind kind = finalKill.Kill.Score.Kind;
        FinalKillFlags flags = kind switch
        {
            DeathKind.FALL => FinalKillFlags.FALL,
            DeathKind.SUICIDE => FinalKillFlags.SUICIDE,
            DeathKind.TEAM_KILL => FinalKillFlags.TEAM_KILL,
            _ => FinalKillFlags.NONE,
        };
        if (finalKill.Kill.Owned)
            flags |= FinalKillFlags.OWNED;

        Death death = finalKill.Death;
        Explosion? explosion = finalKill.Explosion;
        if (explosion != null)
            flags |= FinalKillFlags.EXPLOSION;
        int impactX = explosion?.X ?? (int)death.Position.X;
        int impactY = explosion?.Y ?? (int)death.Position.Y;
        return new FinalKillMsg(
            finalKill.Tick,
            finalKill.Kill.Score.KillerId,
            finalKill.Kill.Score.VictimId,
            flags,
            PackCoordinate((int)death.Position.X),
            PackCoordinate((int)death.Position.Y),
            PackCoordinate(impactX),
            PackCoordinate(impactY),
            PackRadius(explosion?.Radius ?? 0));
    }
}
