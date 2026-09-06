using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Replication;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Modes;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>Owns the ordered match systems and their match-lifetime state.</summary>
public sealed class MatchRuntime : IDisposable
{
    private readonly SortedDictionary<int, Player> _seated = [];
    private readonly ScoringStep _scoring;
    private readonly GameMode _mode;
    private readonly ParticipationStep _participation;
    private readonly GameEventsStep _gameEvents;
    private readonly EndingStep _ending;
    private bool _disposed;

    public int SeatedPlayerCount => _seated.Count;

    public MatchRuntime(TerrainMask terrain, MatchConfig config, int victoryLapTicks,
        MatchStateKeys keys, IReadOnlyList<SpawnPoint>? spawnPoints = null,
        MapZones? zones = null, GameMode? mode = null)
    {
        Generation = keys.Generation;
        World = new SimWorld(terrain, config, spawnPoints, zones);
        Context = new MatchContext(World, _seated);

        _participation = new ParticipationStep(keys);
        _scoring = new ScoringStep(World.Config.Rules.ToMutable(), keys, _seated);
        _mode = mode ?? GameMode.Create(World.Config.Rules);
        _gameEvents = new GameEventsStep(
            new GameEventJudge(keys, _seated, _scoring.TeamOf));
        _ending = new EndingStep(victoryLapTicks);
    }

    public int Generation { get; }

    public MatchContext Context { get; }

    public SimWorld World { get; }

    public MatchStage Stage => Context.Stage;

    public Victor? Winner => _ending.Winner;

    public FinalKillEvent? FinalKill => _ending.FinalKill;

    public MatchConfigSnapshot Config => World.Config;

    public TeamKills TeamKills => _scoring.TeamKills;

    public PlayerScore ScoreOf(Player player) => _scoring.ScoreOf(player);

    public IReadOnlyList<SeatedScore> ScoreRows() => _scoring.Rows();

    /// <summary>Lobby-assigned teams carry into the match (frozen like every
    /// other rule); anyone without one (late joiners) lands on the smallest.</summary>
    public Team? Seat(Player player, Team? lobbyTeam = null)
    {
        EnsureOpen();
        Team? team = null;
        if (Config.Rules.Teams)
        {
            team = lobbyTeam ?? Teams.Smallest(
                World.Players.Values.Select(member => member.Team));
        }

        World.AddPlayer(player.PeerId, team);
        _seated[player.PeerId] = player;
        _scoring.Seat(player, team);
        _participation.Seat(player);
        return team;
    }

    public void AddJipSpectator(Player player)
    {
        EnsureOpen();
        _participation.AddJipSpectator(player);
    }

    public MatchParticipation ParticipationOf(Player player) =>
        _participation.Of(player);

    public PlayerPresentationState PresentationOf(in PlayerState simulation)
    {
        int peerId = simulation.PeerId;
        Player member = _seated[peerId];
        PlayerStats stats = World.Stats[peerId];

        byte spreeMagnitude = _gameEvents.KillingSpreeMagnitude(member);
        bool isBleeding = simulation.IsAtCriticalHealth(stats.MaxHealth);

        return new PlayerPresentationState(spreeMagnitude, isBleeding);
    }

    public void Remove(Player player)
    {
        EnsureOpen();
        World.RemovePlayer(player.PeerId);
        _seated.Remove(player.PeerId);
        _gameEvents.PlayerLeft(player);
    }

    public void EnqueueInput(Player player, int seq, PlayerInput input)
    {
        EnsureOpen();
        if (Stage == MatchStage.PLAYING)
        {
            World.EnqueueInput(player.PeerId, seq, input);
        }
    }

    public MatchUpdate Advance(ServerTime time)
    {
        EnsureOpen();
        if (Stage == MatchStage.VICTORY_LAP)
        {
            return new MatchUpdate(World.Tick, time, [], [], [], [], [],
                _mode.Standing(new GameModeContext(World, _scoring.Rows(), _scoring.TeamKills, _scoring.TeamDeaths)), [], [], null, null, _ending.AdvanceVictoryLap(Context));
        }

        World.Step();
        GameModeUpdate mode = _mode.Advance(Context, _scoring, World.Deaths);
        IReadOnlyList<MatchParticipationChange> participation =
            _participation.Apply(Context, World.Deaths);
        IReadOnlyList<Judgment> judgments = _gameEvents.Judge(mode.Eliminations, World.Tick);
        EndingOutput ending = _ending.Apply(Context, mode.Outcome);
        return new MatchUpdate(World.Tick, time, [.. World.MortarEvents],
            [.. World.Explosions], [.. World.ShellRetirements], [.. World.Deaths],
            [.. mode.Eliminations], mode.Standing, [.. judgments], [.. participation],
            ending.Winner, ending.FinalKill, false, [.. World.DrainModifierChanges()]);
    }

    /// <summary>Players credited with the win: the winner itself, or everyone
    /// still in the match on the winning team.</summary>
    public Player[] Winners(Victor victor) => victor switch
    {
        Victor.Team team =>
        [
            .. _seated.Values
                .Where(member => _scoring.TeamOf(member) == team.Value)
        ],
        Victor.Player player => _seated.GetValueOrDefault(player.PeerId) is Player winner
            ? [winner]
            : [],
        _ => throw new ArgumentOutOfRangeException(nameof(victor)),
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
