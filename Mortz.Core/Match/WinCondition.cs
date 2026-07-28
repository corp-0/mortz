namespace Mortz.Core.Match;

/// <summary>Which predicate decides the winner. Teams controls whether score
/// predicates aggregate player rows into team totals. In authoring TOML:
/// "kills" (case-insensitive).</summary>
public enum WinCondition : byte
{
    KILLS = 0,
}
