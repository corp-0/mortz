using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

public sealed class MatchSession
{
    private const int MATCH_POINT_REMAINING = 1;

    /// <summary>How long a death holds the camera before spectating starts.</summary>
    public const int DEATH_VIEW_DURATION_TICKS = SimConfig.TICK_RATE * 2;

    private readonly int _victoryLapTicks;
    private readonly SortedDictionary<int, Player> _seated = [];
    private readonly MatchStateKey<ParticipationState> _participationKey;
    private readonly GameEventJudge _judge;
    private bool _firstBloodClaimed;
    private int _ticksUntilLobby;
    private MatchPoint? _matchPoint;

    public MatchSession(TerrainMask terrain, MatchConfig config, int victoryLapTicks,
        MatchStateKeys keys, IReadOnlyList<Vec2>? spawnPoints = null, MapZones? zones = null)
    {
        _victoryLapTicks = Math.Max(1, victoryLapTicks);
        World = new SimWorld(terrain, config, spawnPoints, zones);
        _participationKey = keys.Claim<ParticipationState>();
        Scores = new MatchScores(config.Rules, keys, _seated);
        _judge = new GameEventJudge(keys, _seated, member => Scores.Of(member).Team);
    }

    public SimWorld World { get; }
    public MatchScores Scores { get; }
    public TerrainHistory TerrainHistory { get; } = new();
    public MatchStage Stage { get; private set; } = MatchStage.PLAYING;
    public Victor? Winner { get; private set; }
    public FinalKillEvent? FinalKill { get; private set; }
    public MatchConfig Config => World.Config;

    /// <summary>Lobby-assigned teams carry into the match (frozen like every
    /// other rule); anyone without one (late joiners) lands on the smallest.</summary>
    public Team? AddPlayer(Player player, Team? lobbyTeam = null)
    {
        Team? team = null;
        if (Config.Rules.Teams)
            team = lobbyTeam ?? Teams.Smallest(World.Players.Values.Select(member => member.Team));
        World.AddPlayer(player.PeerId, team, player.Skin);
        _seated[player.PeerId] = player;
        Scores.Seat(player, team);
        player.State(_participationKey).Current = MatchParticipation.Active;
        return team;
    }

    public void AddJipSpectator(Player player) =>
        player.State(_participationKey).Current = MatchParticipation.JipSpectator;

    public MatchParticipation ParticipationOf(Player player) =>
        player.State(_participationKey).Current;

    public void RemovePlayer(Player player)
    {
        World.RemovePlayer(player.PeerId);
        _seated.Remove(player.PeerId);
        _judge.PlayerLeft(player);
    }

    /// <summary>For catching up late joiners; live changes ride MatchFrame.</summary>
    public MatchPoint? ActiveMatchPoint => _matchPoint;

    public void EnqueueInput(Player player, int seq, PlayerInput input)
    {
        if (Stage == MatchStage.PLAYING)
            World.EnqueueInput(player.PeerId, seq, input);
    }

    public MatchFrame Step()
    {
        if (Stage == MatchStage.VICTORY_LAP)
        {
            bool returnToLobby = --_ticksUntilLobby <= 0;
            return new MatchFrame(
                World.Tick, [], [], [], [], [], [], [], null, null, null, returnToLobby);
        }

        World.Step();

        Explosion[] explosions = World.Explosions.ToArray();
        foreach (Explosion explosion in explosions)
        {
            TerrainHistory.Record(explosion.X, explosion.Y, explosion.Radius);
        }

        Death[] deaths = World.Deaths.ToArray();
        List<ScoredKill> eliminations = new();
        Victor? matchEnded = null;
        FinalKillEvent? finalKill = null;
        foreach (Death death in deaths)
        {
            if (ScoreDeath(death) is not ScoredKill elimination)
                continue;
            eliminations.Add(elimination);
            if (matchEnded != null || elimination.Score.Winner is not Victor winner) continue;
            matchEnded = winner;
            Explosion? explosion = FindExplosion(death, explosions);
            finalKill = new FinalKillEvent(World.Tick, elimination, death, explosion);
            FinalKill = finalKill;
        }

        MatchParticipationChange[] participationChanges =
            UpdateParticipation(deaths).ToArray();

        return new MatchFrame(
            World.Tick,
            World.MortarEvents.ToArray(),
            explosions,
            World.ShellRetirements.ToArray(),
            deaths,
            eliminations.ToArray(),
            _judge.JudgeFrame(eliminations, World.Tick).ToArray(),
            participationChanges,
            CheckMatchPoint(),
            matchEnded,
            finalKill,
            false);
    }

    private List<MatchParticipationChange> UpdateParticipation(IReadOnlyList<Death> deaths)
    {
        List<MatchParticipationChange> changes = [];
        foreach ((int peerId, Player member) in _seated)
        {
            PlayerState player = World.Players[peerId];
            ParticipationState state = member.State(_participationKey);
            MatchParticipation current = state.Current;
            if (player.RespawnTicks == 0 && current.Activity != MatchActivity.ACTIVE)
            {
                ChangeParticipation(peerId, state, MatchParticipation.Active, changes);
                state.SpectateAtTick = null;
                continue;
            }
            if (player.RespawnTicks == 0 || current.Activity != MatchActivity.DEATH_PRESENTATION ||
                state.SpectateAtTick is not int spectateAt ||
                World.Tick < spectateAt)
            {
                continue;
            }
            MatchParticipation spectating = new(
                MatchSeat.PLAYER,
                MatchActivity.SPECTATING,
                SpectateReason.RESPAWN,
                current.ReturnTick);
            ChangeParticipation(peerId, state, spectating, changes);
            state.SpectateAtTick = null;
        }

        foreach (Death death in deaths)
        {
            if (!World.Players.TryGetValue(death.PeerId, out PlayerState player))
                continue;
            ParticipationState state = _seated[death.PeerId].State(_participationKey);
            int respawnAtTick = World.Tick + player.RespawnTicks;
            MatchParticipation presentation = new(
                MatchSeat.PLAYER,
                MatchActivity.DEATH_PRESENTATION,
                SpectateReason.RESPAWN,
                respawnAtTick);
            ChangeParticipation(death.PeerId, state, presentation, changes);
            if (Config.Rules.SpectateDuringRespawn)
            {
                ScheduleSpectator(state, player);
            }
        }
        return changes;
    }

    private void ScheduleSpectator(ParticipationState state, PlayerState player)
    {
        if (player.RespawnTicks < DEATH_VIEW_DURATION_TICKS + SimConfig.TICK_RATE * 3) return;
        state.SpectateAtTick = World.Tick + DEATH_VIEW_DURATION_TICKS;
    }

    private static void ChangeParticipation(int peerId, ParticipationState state,
        MatchParticipation next, List<MatchParticipationChange> changes)
    {
        if (state.Current == next)
            return;
        state.Current = next;
        changes.Add(new MatchParticipationChange(peerId, next));
    }

    public ScoredKill? ScoreDeath(Death death)
    {
        if (Stage != MatchStage.PLAYING)
            return null;
        if (_seated.GetValueOrDefault(death.PeerId) is not Player victim)
            return null;
        Player? killer = ResolveKiller(death, victim);
        DeathScore result = Scores.ScoreDeath(
            death.KillerId, victim, killer, NearestEnemy(death));
        bool firstBlood = !_firstBloodClaimed && result.CreditedKill;
        if (firstBlood)
            _firstBloodClaimed = true;
        ScoredKill kill = new(killer, victim, result, death.Owned, firstBlood, death.ShellId);
        if (result.Winner is Victor winner)
        {
            Stage = MatchStage.VICTORY_LAP;
            Winner = winner;
            _ticksUntilLobby = _victoryLapTicks;
        }

        return kill;
    }

    private Player? ResolveKiller(Death death, Player victim)
    {
        if (death.KillerId == 0)
            return null;
        if (death.KillerId == death.PeerId)
            return victim;
        return _seated.GetValueOrDefault(death.KillerId);
    }

    /// <summary>The living enemy nearest the death spot, null when there is
    /// none. Teammates never count.</summary>
    private Player? NearestEnemy(Death death)
    {
        Team? victimTeam = World.Players.TryGetValue(death.PeerId, out PlayerState victim)
            ? victim.Team
            : null;
        Player? closest = null;
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
                closest = _seated[peerId];
            }
        }
        return closest;
    }

    /// <summary>Recomputed every frame, so leavers and suicides update it too;
    /// only entering and leaving match point gets announced.</summary>
    private MatchPointChange? CheckMatchPoint()
    {
        MatchStanding standing = Scores.Standing();
        bool active = Stage == MatchStage.PLAYING &&
                      standing.Remaining == MATCH_POINT_REMAINING;
        if (active == (_matchPoint != null))
            return null;
        _matchPoint = active ? new MatchPoint(standing.Remaining, standing.Leader) : null;
        return new MatchPointChange(_matchPoint);
    }

    /// <summary>Players credited with the win: the winner itself, or everyone
    /// still in the match on the winning team.</summary>
    public Player[] Winners(Victor victor) => victor switch
    {
        Victor.Team team => _seated.Values
            .Where(member => Scores.Of(member).Team == team.Value)
            .ToArray(),
        Victor.Player player => _seated.GetValueOrDefault(player.PeerId) is Player winner
            ? [winner]
            : [],
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
