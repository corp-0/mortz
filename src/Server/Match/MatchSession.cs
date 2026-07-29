using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;

namespace Mortz.Server.Match;

internal enum MatchStage
{
    PLAYING,
    VICTORY_LAP,
}

internal readonly record struct ScoredElimination(
    Scoreboard.DeathResult Score,
    bool Owned,
    bool FirstBlood);

internal readonly record struct FinalKillEvent(
    int Tick,
    ScoredElimination Elimination,
    Death Death,
    Explosion? Explosion);

/// <summary>Held is the new state, null when match point just lapsed.</summary>
internal readonly record struct MatchPointChange(MatchPoint? Held);

internal readonly record struct MatchFrame(
    int Tick,
    SimWorld.MortarEvent[] MortarEvents,
    Explosion[] Explosions,
    ShellRetirement[] ShellRetirements,
    Death[] Deaths,
    ScoredElimination[] Eliminations,
    GameEventJudge.Judgment[] GameEvents,
    MatchPointChange? MatchPoint,
    Victor? MatchEnded,
    FinalKillEvent? FinalKill,
    bool ReturnToLobby);

/// <summary>All state whose lifetime is exactly one match. It advances the
/// simulation and turns raw deaths into authoritative scoring outcomes without
/// knowing anything about Godot nodes or network messages.</summary>
internal sealed class MatchSession
{
    private const int MATCH_POINT_REMAINING = 1;

    private readonly int _victoryLapTicks;
    private readonly FirstBloodTracker _firstBlood = new();
    private readonly GameEventJudge _judge = new();
    private int _ticksUntilLobby;
    private MatchPoint? _matchPoint;

    public SimWorld World { get; }
    public Scoreboard Scores { get; }
    public TerrainHistory TerrainHistory { get; } = new();
    public MatchStage Stage { get; private set; } = MatchStage.PLAYING;
    public Victor? Winner { get; private set; }
    public FinalKillEvent? FinalKill { get; private set; }
    public MatchConfig Config => World.Config;

    public MatchSession(TerrainMask terrain, MatchConfig config, int seed, int victoryLapTicks,
        IReadOnlyList<Vec2>? spawnPoints = null)
    {
        World = new SimWorld(terrain, config, seed, spawnPoints);
        Scores = new Scoreboard(config.Rules);
        _victoryLapTicks = Math.Max(1, victoryLapTicks);
    }

    /// <summary>Lobby-assigned teams carry into the match (frozen like every
    /// other rule); anyone without one (late joiners) lands on the smallest.</summary>
    public Team? AddPlayer(int peerId, Team? lobbyTeam = null)
    {
        Team? team = null;
        if (Config.Rules.Teams)
            team = lobbyTeam ?? Teams.Smallest(World.Players.Values.Select(player => player.Team));
        World.AddPlayer(peerId, team);
        Scores.AddPlayer(peerId, team);
        return team;
    }

    public void RemovePlayer(int peerId)
    {
        World.RemovePlayer(peerId);
        Scores.RemovePlayer(peerId);
        _judge.RemovePlayer(peerId);
    }

    /// <summary>For catching up late joiners; live changes ride MatchFrame.</summary>
    public MatchPoint? ActiveMatchPoint => _matchPoint;

    public void EnqueueInput(int peerId, int seq, PlayerInput input)
    {
        if (Stage == MatchStage.PLAYING)
            World.EnqueueInput(peerId, seq, input);
    }

    public MatchFrame Step()
    {
        if (Stage == MatchStage.VICTORY_LAP)
        {
            bool returnToLobby = --_ticksUntilLobby <= 0;
            return new MatchFrame(
                World.Tick, [], [], [], [], [], [], null, null, null, returnToLobby);
        }

        World.Step();

        Explosion[] explosions = World.Explosions.ToArray();
        foreach (Explosion explosion in explosions)
        {
            TerrainHistory.Record(explosion.X, explosion.Y, explosion.Radius);
        }

        Death[] deaths = World.Deaths.ToArray();
        List<ScoredElimination> eliminations = new();
        List<GameEventJudge.Kill> kills = new();
        Victor? matchEnded = null;
        FinalKillEvent? finalKill = null;
        foreach (Death death in deaths)
        {
            ScoredElimination? scored = ScoreDeath(death);
            if (scored is not ScoredElimination elimination)
                continue;
            eliminations.Add(elimination);
            kills.Add(new GameEventJudge.Kill(
                elimination.Score.KillerId,
                elimination.Score.VictimId,
                elimination.Score.Kind,
                elimination.Owned,
                elimination.FirstBlood,
                death.ShellId));
            if (matchEnded != null || elimination.Score.Winner is not Victor winner) continue;
            matchEnded = winner;
            Explosion? explosion = FindExplosion(death, explosions);
            finalKill = new FinalKillEvent(World.Tick, elimination, death, explosion);
            FinalKill = finalKill;
        }

        return new MatchFrame(
            World.Tick,
            World.MortarEvents.ToArray(),
            explosions,
            World.ShellRetirements.ToArray(),
            deaths,
            eliminations.ToArray(),
            _judge.JudgeFrame(kills, World.Tick, JudgeTeams(kills)).ToArray(),
            CheckMatchPoint(),
            matchEnded,
            finalKill,
            false);
    }

    internal ScoredElimination? ScoreDeath(Death death)
    {
        if (Stage != MatchStage.PLAYING)
            return null;
        Scoreboard.DeathResult? score = Scores.ScoreDeath(new Scoreboard.Death(
            death.PeerId, death.KillerId, NearestEnemy(death)));
        if (score is not Scoreboard.DeathResult result)
            return null;
        ScoredElimination elimination = new(
            result,
            death.Owned,
            _firstBlood.TryClaim(result.CreditedKill));
        if (result.Winner is Victor winner)
        {
            Stage = MatchStage.VICTORY_LAP;
            Winner = winner;
            _ticksUntilLobby = _victoryLapTicks;
        }

        return elimination;
    }

    /// <summary>The living enemy nearest the death spot, 0 when there is
    /// none. Teammates never count.</summary>
    private int NearestEnemy(Death death)
    {
        Team? victimTeam = World.Players.TryGetValue(death.PeerId, out PlayerState victim)
            ? victim.Team
            : null;
        int closest = 0;
        float best = float.MaxValue;
        foreach ((int peerId, PlayerState player) in World.Players)
        {
            if (peerId == death.PeerId || player.RespawnTicks > 0)
                continue;
            if (Teams.SameSide(victimTeam, player.Team))
                continue;
            float distance = (player.Position - death.Position).LengthSquared();
            if (distance < best)
            {
                best = distance;
                closest = peerId;
            }
        }
        return closest;
    }

    /// <summary>Recomputed every frame so suicide penalties and leavers move
    /// the state too, not just kills. The leader can change while the state
    /// holds; only entering and leaving the state is announced.</summary>
    private MatchPointChange? CheckMatchPoint()
    {
        Scoreboard.MatchStanding standing = Scores.Standing();
        bool active = Stage == MatchStage.PLAYING &&
                      standing.Remaining == MATCH_POINT_REMAINING;
        if (active == (_matchPoint != null))
            return null;
        _matchPoint = active ? new MatchPoint(standing.Remaining, standing.Leader) : null;
        return new MatchPointChange(_matchPoint);
    }

    /// <summary>Skipped on quiet frames; the judge only reads teams when there
    /// are kills.</summary>
    private Dictionary<int, Team>? JudgeTeams(List<GameEventJudge.Kill> kills)
    {
        if (!Config.Rules.Teams || kills.Count == 0)
            return null;
        Dictionary<int, Team> teams = new();
        foreach ((int peerId, PlayerState player) in World.Players)
        {
            if (player.Team is Team team)
                teams[peerId] = team;
        }
        return teams;
    }

    /// <summary>Peers credited with the win: the winner itself, or everyone
    /// still in the match on the winning team.</summary>
    public int[] WinnerPeers(Victor victor) => victor switch
    {
        TeamVictor team => World.Players.Where(pair => pair.Value.Team == team.Team)
            .Select(pair => pair.Key).ToArray(),
        PlayerVictor player => [player.PeerId],
        _ => throw new ArgumentOutOfRangeException(nameof(victor)),
    };

    private static Explosion? FindExplosion(
        Death death, IReadOnlyList<Explosion> explosions)
    {
        Explosion? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Explosion explosion in explosions)
        {
            if (explosion.OwnerId != death.KillerId)
                continue;
            float dx = explosion.X - death.Position.X;
            float dy = explosion.Y - death.Position.Y;
            float distance = dx * dx + dy * dy;
            if (distance >= nearestDistance)
                continue;
            nearest = explosion;
            nearestDistance = distance;
        }

        return nearest;
    }
}
