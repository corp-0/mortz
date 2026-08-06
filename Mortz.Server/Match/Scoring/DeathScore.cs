using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match.Scoring;

/// <summary>The complete result of applying one death; the single source of
/// truth for attribution, nothing may re-classify the same death.</summary>
public readonly record struct DeathScore(
    int KillerId,
    int VictimId,
    DeathKind Kind,
    PlayerScore? Killer,
    PlayerScore Victim,
    KillReward? Reward,
    TeamKills TeamKills,
    Victor? Winner)
{
    public bool CreditedKill => Kind == DeathKind.KILL;
}
