using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Events;

namespace Mortz.Server.Match;

public readonly record struct FinalKillEvent(
    int Tick,
    ScoredKill Kill,
    Death Death,
    Explosion? Explosion);

public readonly record struct MatchParticipationChange(
    int PeerId,
    MatchParticipation State);

/// <summary>Held is the new state, null when match point just lapsed.</summary>
public readonly record struct MatchPointChange(MatchPoint? Held);

public readonly record struct MatchFrame(
    int Tick,
    SimWorld.MortarEvent[] MortarEvents,
    Explosion[] Explosions,
    ShellRetirement[] ShellRetirements,
    Death[] Deaths,
    ScoredKill[] Eliminations,
    Judgment[] GameEvents,
    MatchParticipationChange[] ParticipationChanges,
    MatchPointChange? MatchPoint,
    Victor? MatchEnded,
    FinalKillEvent? FinalKill,
    bool ReturnToLobby);
