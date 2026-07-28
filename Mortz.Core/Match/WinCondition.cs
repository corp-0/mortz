namespace Mortz.Core.Match;

/// <summary>What the score predicate reads: individual rows or team totals.
/// In authoring TOML: "player_kills" / "team_kills" (case-insensitive).</summary>
public enum WinCondition : byte
{
    PLAYER_KILLS = 0,
    TEAM_KILLS = 1,
}
