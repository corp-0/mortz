namespace Mortz.Core.Match.WinConditions;

/// <summary>Read-only match scoring state available to a win condition.</summary>
public sealed class WinConditionContext
{
    private readonly int[] _teamKills;

    public ModeRules Rules { get; }
    public IReadOnlyDictionary<int, Scoreboard.Row> Rows { get; }
    public int TeamCount => _teamKills.Length - 1;

    internal WinConditionContext(
        ModeRules rules,
        IReadOnlyDictionary<int, Scoreboard.Row> rows,
        int[] teamKills)
    {
        Rules = rules;
        Rows = rows;
        _teamKills = teamKills;
    }

    public int TeamKills(byte teamId) =>
        teamId < _teamKills.Length ? _teamKills[teamId] : 0;
}
