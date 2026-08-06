using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Events;

namespace Mortz.Server.Match;

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
