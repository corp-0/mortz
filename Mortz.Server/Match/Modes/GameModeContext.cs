using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match.Modes;

public class GameModeContext(SimWorld world, IReadOnlyList<SeatedScore> rows, TeamKills teamKills,
    TeamDeaths teamDeaths = default)
{
    public SimWorld World { get; } = world;
    public int Tick => World.Tick;
    public ModeRulesSnapshot Rules => World.Config.Rules;
    public IReadOnlyList<SeatedScore> Rows { get; } = rows;
    public TeamKills TeamKills { get; } = teamKills;
    public TeamDeaths TeamDeaths { get; } = teamDeaths;
}
