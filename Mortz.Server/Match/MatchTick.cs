using System.Collections.Immutable;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match;

/// <summary>Typed, write-once outputs passed between stages of one match tick.</summary>
public class MatchTick(MatchContext match, ServerTime time)
{
    private readonly Output<SimulationOutput> _simulation = new(nameof(SimulationOutput));
    private readonly Output<ScoringOutput> _scoring = new(nameof(ScoringOutput));
    private readonly Output<ImmutableArray<MatchParticipationChange>> _participationChanges =
        new(nameof(ParticipationChanges));
    private readonly Output<ImmutableArray<Judgment>> _gameEvents = new(nameof(GameEvents));
    private readonly Output<EndingOutput> _ending = new(nameof(EndingOutput));
    private readonly Output<bool> _returnToLobby = new(nameof(ReturnToLobby));
    private bool _completed;

    public MatchContext Match { get; } = match;

    public ServerTime Time { get; } = time;

    public IReadOnlyList<SimWorld.MortarEvent> MortarEvents => _simulation.Value.MortarEvents;

    public IReadOnlyList<Explosion> Explosions => _simulation.Value.Explosions;

    public IReadOnlyList<ShellRetirement> ShellRetirements =>
        _simulation.Value.ShellRetirements;

    public IReadOnlyList<Death> Deaths => _simulation.Value.Deaths;

    public IReadOnlyList<ScoredKill> Eliminations => _scoring.Value.Eliminations;

    public MatchStanding Standing => _scoring.Value.Standing;

    public WinningScore? WinningScore => _scoring.Value.WinningScore;

    public IReadOnlyList<MatchParticipationChange> ParticipationChanges =>
        _participationChanges.Value;

    public IReadOnlyList<Judgment> GameEvents => _gameEvents.Value;

    public Victor? MatchEnded => _ending.Value.MatchEnded;

    public FinalKillEvent? FinalKill => _ending.Value.FinalKill;

    public bool ReturnToLobby => _returnToLobby.Value;

    public void SetSimulationOutputs(
        IReadOnlyList<SimWorld.MortarEvent> mortarEvents,
        IReadOnlyList<Explosion> explosions,
        IReadOnlyList<ShellRetirement> shellRetirements,
        IReadOnlyList<Death> deaths)
    {
        EnsureOpen();
        _simulation.Set(new SimulationOutput(
            [.. mortarEvents], [.. explosions], [.. shellRetirements], [.. deaths]));
    }

    public void SetScoring(
        IReadOnlyList<ScoredKill> eliminations,
        MatchStanding standing,
        WinningScore? winningScore)
    {
        EnsureOpen();
        _scoring.Set(new ScoringOutput([.. eliminations], standing, winningScore));
    }

    public void SetParticipationChanges(IReadOnlyList<MatchParticipationChange> changes)
    {
        EnsureOpen();
        _participationChanges.Set([.. changes]);
    }

    public void SetGameEvents(IReadOnlyList<Judgment> events)
    {
        EnsureOpen();
        _gameEvents.Set([.. events]);
    }

    public void SetEnding(Victor? winner, FinalKillEvent? finalKill)
    {
        EnsureOpen();
        _ending.Set(new EndingOutput(winner, finalKill));
    }

    public void SetReturnToLobby(bool returnToLobby)
    {
        EnsureOpen();
        _returnToLobby.Set(returnToLobby);
    }

    public MatchUpdate Complete()
    {
        EnsureOpen();
        SimulationOutput simulation = _simulation.Value;
        ScoringOutput scoring = _scoring.Value;
        ImmutableArray<ScoredKill> eliminations = scoring.Eliminations;
        ImmutableArray<Judgment> gameEvents = _gameEvents.Value;
        ImmutableArray<MatchParticipationChange> participationChanges =
            _participationChanges.Value;
        EndingOutput ending = _ending.Value;
        bool returnToLobby = _returnToLobby.Value;
        _completed = true;
        return new MatchUpdate(
            Match.World.Tick,
            Time,
            simulation.MortarEvents,
            simulation.Explosions,
            simulation.ShellRetirements,
            simulation.Deaths,
            eliminations,
            scoring.Standing,
            gameEvents,
            participationChanges,
            ending.MatchEnded,
            ending.FinalKill,
            returnToLobby);
    }

    private void EnsureOpen()
    {
        if (_completed)
            throw new InvalidOperationException("The match tick is already complete.");
    }

    private readonly record struct SimulationOutput(
        ImmutableArray<SimWorld.MortarEvent> MortarEvents,
        ImmutableArray<Explosion> Explosions,
        ImmutableArray<ShellRetirement> ShellRetirements,
        ImmutableArray<Death> Deaths);

    private readonly record struct ScoringOutput(
        ImmutableArray<ScoredKill> Eliminations,
        MatchStanding Standing,
        WinningScore? WinningScore);

    private readonly record struct EndingOutput(Victor? MatchEnded, FinalKillEvent? FinalKill);

    private class Output<T>(string name)
    {
        private T _value = default!;
        private bool _set;

        public T Value => _set
            ? _value
            : throw new InvalidOperationException($"{name} has not been produced.");

        public void Set(T value)
        {
            if (_set)
                throw new InvalidOperationException($"{name} has already been produced.");
            _value = value;
            _set = true;
        }
    }
}
