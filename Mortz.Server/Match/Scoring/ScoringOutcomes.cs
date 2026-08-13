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

/// <summary>A suicide's kill handed to an enemy; Kills is their tally after.</summary>
public readonly record struct KillReward(int PeerId, int Kills);

/// <summary>Who is closest to winning and how much they still need.</summary>
public readonly record struct MatchStanding(Victor? Leader, int Remaining);
