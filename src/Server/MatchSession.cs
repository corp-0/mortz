using Mortz.Core;

namespace Mortz.Server;

internal enum MatchStage
{
    Playing,
    VictoryLap,
}

internal readonly record struct ServerExplosion(
    int X, int Y, int Radius, int OwnerId, int SpawnSeq);

internal readonly record struct ServerDeath(
    int PeerId, Vec2 Position, int KillerId, bool Owned);

internal readonly record struct ScoredElimination(
    Scoreboard.DeathResult Score, bool Owned, bool FirstBlood);

internal readonly record struct MatchFrame(
    int Tick,
    SimWorld.MortarEvent[] MortarEvents,
    ServerExplosion[] Explosions,
    (int FiredBy, int SpawnSeq)[] ShellRetirements,
    ServerDeath[] Deaths,
    ScoredElimination[] Eliminations,
    Scoreboard.MatchWinner? MatchEnded,
    bool ReturnToLobby);

/// <summary>All state whose lifetime is exactly one match. It advances the
/// simulation and turns raw deaths into authoritative scoring outcomes without
/// knowing anything about Godot nodes or network messages.</summary>
internal sealed class MatchSession
{
    private readonly int _victoryLapTicks;
    private readonly FirstBloodTracker _firstBlood = new();
    private int _ticksUntilLobby;

    public SimWorld World { get; }
    public Scoreboard Scores { get; }
    public TerrainHistory TerrainHistory { get; } = new();
    public MatchStage Stage { get; private set; } = MatchStage.Playing;
    public Scoreboard.MatchWinner? Winner { get; private set; }
    public MatchConfig Config => World.Config;

    public MatchSession(TerrainMask terrain, MatchConfig config, int seed, int victoryLapTicks)
    {
        World = new SimWorld(terrain, config, seed);
        Scores = new Scoreboard(config);
        _victoryLapTicks = Math.Max(1, victoryLapTicks);
    }

    public byte AddPlayer(int peerId)
    {
        byte team = NextTeam();
        World.AddPlayer(peerId, team);
        Scores.AddPlayer(peerId, team);
        return team;
    }

    public void RemovePlayer(int peerId)
    {
        World.RemovePlayer(peerId);
        Scores.RemovePlayer(peerId);
    }

    public void EnqueueInput(int peerId, int seq, PlayerInput input) =>
        World.EnqueueInput(peerId, seq, input);

    public MatchFrame Step()
    {
        World.Step();

        ServerExplosion[] explosions = World.Explosions
            .Select(e => new ServerExplosion(e.X, e.Y, e.Radius, e.OwnerId, e.SpawnSeq))
            .ToArray();
        foreach (ServerExplosion explosion in explosions)
            TerrainHistory.Record(explosion.X, explosion.Y, explosion.Radius);

        ServerDeath[] deaths = World.Deaths
            .Select(d => new ServerDeath(d.PeerId, d.Position, d.KillerId, d.Owned))
            .ToArray();
        List<ScoredElimination> eliminations = new();
        Scoreboard.MatchWinner? matchEnded = null;
        foreach (ServerDeath death in deaths)
        {
            ScoredElimination? scored = ScoreDeath(death);
            if (scored is not { } elimination)
                continue;
            eliminations.Add(elimination);
            matchEnded ??= elimination.Score.Winner;
        }

        bool returnToLobby = Stage == MatchStage.VictoryLap && --_ticksUntilLobby == 0;
        return new MatchFrame(
            World.Tick,
            World.MortarEvents.ToArray(),
            explosions,
            World.ShellRetirements.ToArray(),
            deaths,
            eliminations.ToArray(),
            matchEnded,
            returnToLobby);
    }

    internal ScoredElimination? ScoreDeath(ServerDeath death)
    {
        if (Stage != MatchStage.Playing)
            return null;
        Scoreboard.DeathResult? score = Scores.ScoreDeath(death.PeerId, death.KillerId);
        if (score is not { } result)
            return null;
        ScoredElimination elimination = new(
            result,
            death.Owned,
            _firstBlood.TryClaim(result.CreditedKill));
        if (result.Winner is { } winner)
        {
            Stage = MatchStage.VictoryLap;
            Winner = winner;
            _ticksUntilLobby = _victoryLapTicks;
        }
        return elimination;
    }

    public ServerExplosion? DebugCarve(int x, int y)
    {
        List<(int X, int Y)> removed = World.Terrain.CarveCircle(x, y, SimConfig.DEBUG_CARVE_RADIUS);
        if (removed.Count == 0)
            return null;
        ServerExplosion explosion = new(x, y, SimConfig.DEBUG_CARVE_RADIUS, 0, -1);
        TerrainHistory.Record(x, y, SimConfig.DEBUG_CARVE_RADIUS);
        return explosion;
    }

    private byte NextTeam()
    {
        if (!Config.Teams)
            return 0;
        int one = World.Players.Values.Count(player => player.TeamId == 1);
        int two = World.Players.Values.Count(player => player.TeamId == 2);
        return (byte)(one <= two ? 1 : 2);
    }
}
