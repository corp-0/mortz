using Mortz.Core.Match.Scoring;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Server.Match.Scoring.WinConditions;

public sealed class WinConditionContext(
    ModeRules rules,
    IReadOnlyList<SeatedScore> rows,
    TeamKills teamKills)
{
    public ModeRules Rules { get; } = rules;
    public IReadOnlyList<SeatedScore> Rows { get; } = rows;
    public TeamKills TeamKills { get; } = teamKills;
}
