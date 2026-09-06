using Mortz.Core.Match.Teams;

namespace Mortz.Core.Match.Scoring;

public readonly record struct TeamDeaths(int Blue, int Red)
{
    public int this[Team team] => team switch
    {
        Team.BLUE => Blue,
        Team.RED => Red,
        _ => throw new ArgumentOutOfRangeException(nameof(team)),
    };

    public TeamDeaths Add(Team team) => team switch
    {
        Team.BLUE => this with { Blue = Blue + 1 },
        Team.RED => this with { Red = Red + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(team)),
    };
}
