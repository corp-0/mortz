namespace Mortz.Core.Match;

public static class Teams
{
    public static readonly IReadOnlyList<Team> ALL = [Team.BLUE, Team.RED];

    public static string Name(Team team) => team switch
    {
        Team.BLUE => "Team Blue",
        Team.RED => "Team Red",
        _ => throw new ArgumentOutOfRangeException(nameof(team)),
    };

    /// <summary>Unassigned players share no side, not even with each other.</summary>
    public static bool SameSide(Team? a, Team? b) => a is Team team && b == team;

    /// <summary>The thinnest team, ties to the first in ALL.</summary>
    public static Team Smallest(IEnumerable<Team?> assignments)
    {
        List<Team?> taken = assignments.ToList();
        return ALL.MinBy(team => taken.Count(assignment => assignment == team));
    }

    /// <summary>Alternating assignment by roster index.</summary>
    public static Team Deal(int index) => ALL[index % ALL.Count];
}
