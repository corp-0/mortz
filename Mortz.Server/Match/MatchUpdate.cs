using System.Collections.Immutable;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match;

/// <summary>Frozen outputs from one completed match tick.</summary>
public class MatchUpdate
{
    internal MatchUpdate(
        int tick,
        ServerTime time,
        ImmutableArray<SimWorld.MortarEvent> mortarEvents,
        ImmutableArray<Explosion> explosions,
        ImmutableArray<ShellRetirement> shellRetirements,
        ImmutableArray<Death> deaths,
        ImmutableArray<ScoredKill> eliminations,
        MatchStanding standing,
        ImmutableArray<Judgment> gameEvents,
        ImmutableArray<MatchParticipationChange> participationChanges,
        Victor? matchEnded,
        FinalKillEvent? finalKill,
        bool returnToLobby)
    {
        Tick = tick;
        Time = time;
        MortarEvents = mortarEvents;
        Explosions = explosions;
        ShellRetirements = shellRetirements;
        Deaths = deaths;
        Eliminations = eliminations;
        Standing = standing;
        GameEvents = gameEvents;
        ParticipationChanges = participationChanges;
        MatchEnded = matchEnded;
        FinalKill = finalKill;
        ReturnToLobby = returnToLobby;
    }

    public int Tick { get; }

    public ServerTime Time { get; }

    public ImmutableArray<SimWorld.MortarEvent> MortarEvents { get; }

    public ImmutableArray<Explosion> Explosions { get; }

    public ImmutableArray<ShellRetirement> ShellRetirements { get; }

    public ImmutableArray<Death> Deaths { get; }

    public ImmutableArray<ScoredKill> Eliminations { get; }

    public MatchStanding Standing { get; }

    public ImmutableArray<Judgment> GameEvents { get; }

    public ImmutableArray<MatchParticipationChange> ParticipationChanges { get; }

    public Victor? MatchEnded { get; }

    public FinalKillEvent? FinalKill { get; }

    public bool ReturnToLobby { get; }
}
