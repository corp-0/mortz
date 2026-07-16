namespace Mortz.Core.Match;

/// <summary>What the score predicate reads: individual rows or team totals.
/// In ruleset JSON: "PLAYER_KILLS" / "TEAM_KILLS" (case-insensitive).</summary>
public enum WinCondition : byte
{
    PLAYER_KILLS = 0,
    TEAM_KILLS = 1,
}
