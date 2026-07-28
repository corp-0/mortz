namespace Mortz.Core.Match;

/// <summary>A short label for a ruleset, derived because there is no mode enum.</summary>
public static class ModeLabel
{
    public static string For(MatchConfig config)
    {
        string objective = config.WinCondition switch
        {
            WinCondition.TEAM_KILLS => "Team Kills",
            _ => "Kills",
        };
        return config.Teams ? $"Teams - {objective}" : objective;
    }
}
