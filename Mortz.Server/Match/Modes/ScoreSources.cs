using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;

namespace Mortz.Server.Match.Modes;

public readonly record struct ContenderScore(Victor Contender, int Value);

public interface IScoreSource
{
    IReadOnlyList<ContenderScore> Read(GameModeContext context);
}

public static class ScoreSources
{
    public static IEnumerable<Team> CompetingTeams(GameModeContext context) =>
        Teams.All.Where(team => context.Rows.Any(row => row.Score.Team == team));

    public static IScoreSource Create(ScoreMetric metric) => metric switch
    {
        ScoreMetric.KILLS => new KillScores(),
        ScoreMetric.DEATHS => new DeathScores(),
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
}

public class KillScores : IScoreSource
{
    public IReadOnlyList<ContenderScore> Read(GameModeContext context) => context.Rules.Teams
        ? ScoreSources.CompetingTeams(context).Select(team => new ContenderScore(new Victor.Team(team), context.TeamKills[team])).ToArray()
        : context.Rows.Select(row => new ContenderScore(new Victor.Player(row.Player.PeerId), row.Score.Kills)).ToArray();
}

public class DeathScores : IScoreSource
{
    public IReadOnlyList<ContenderScore> Read(GameModeContext context) => context.Rules.Teams
        ? ScoreSources.CompetingTeams(context).Select(team => new ContenderScore(new Victor.Team(team), context.TeamDeaths[team])).ToArray()
        : context.Rows.Select(row => new ContenderScore(new Victor.Player(row.Player.PeerId), row.Score.Deaths)).ToArray();
}
