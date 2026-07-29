namespace Mortz.Core.Match;

/// <summary>Totals can go negative: suicide penalties subtract from the team
/// too.</summary>
public readonly record struct TeamKills(int Blue, int Red)
{
    public int this[Team team] => team switch
    {
        Team.BLUE => Blue,
        Team.RED => Red,
        _ => throw new ArgumentOutOfRangeException(nameof(team)),
    };

    public TeamKills Add(Team team, int delta) => team switch
    {
        Team.BLUE => this with { Blue = Blue + delta },
        Team.RED => this with { Red = Red + delta },
        _ => throw new ArgumentOutOfRangeException(nameof(team)),
    };
}
