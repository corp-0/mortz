namespace Mortz.Core.Match.WinConditions;

/// <summary>Read-only match scoring state available to a win condition.</summary>
public sealed class WinConditionContext
{
    public ModeRules Rules { get; }
    public IReadOnlyDictionary<int, Scoreboard.Row> Rows { get; }
    public TeamKills TeamKills { get; }

    internal WinConditionContext(
        ModeRules rules,
        IReadOnlyDictionary<int, Scoreboard.Row> rows,
        TeamKills teamKills)
    {
        Rules = rules;
        Rows = rows;
        TeamKills = teamKills;
    }
}
