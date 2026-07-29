using Godot;
using Mortz.Core.Match;

namespace Mortz.Client.Ui;

/// <summary>The two v1 team colors, shared by the lobby columns, the team
/// score HUD, and player nameplates so a team reads the same everywhere.</summary>
public static class TeamColors
{
    public static readonly Color Blue = new("60a5fa");
    public static readonly Color Red = new("f87171");

    public static Color For(Team team) => team switch
    {
        Team.BLUE => Blue,
        Team.RED => Red,
        _ => throw new ArgumentOutOfRangeException(nameof(team)),
    };
}
