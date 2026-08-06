namespace Mortz.Core.Match.Scoring;

/// <summary>Selects the strategy that decides the winner. Teams controls
/// whether score strategies evaluate player rows or team totals. Authoring
/// values are case-insensitive snake case.</summary>
public enum WinCondition : byte
{
    KILLS = 0,
    KILL_LEAD = 1,
}
