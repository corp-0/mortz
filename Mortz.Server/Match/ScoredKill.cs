using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>One scored death with its full context: the players involved, the
/// scoring result, and the shell. Killer is null when nobody present gets the
/// credit (a death pit or a killer who already left); ShellId is -1 when no
/// shell was involved.</summary>
public readonly record struct ScoredKill(
    Player? Killer,
    Player Victim,
    DeathScore Score,
    bool Owned,
    bool FirstBlood,
    int ShellId)
{
    public DeathKind Kind => Score.Kind;
}

/// <summary>The scored death that decided the match.</summary>
public readonly record struct WinningScore(Death Death, ScoredKill Elimination);
