using Godot;

namespace Mortz.Client.Ui;

/// <summary>The two v1 team colors, shared by the lobby columns, the team
/// score HUD, and player nameplates so a team reads the same everywhere.</summary>
public static class TeamColors
{
    public static readonly Color Team1 = new("60a5fa");
    public static readonly Color Team2 = new("f87171");

    public static Color For(byte teamId) => teamId switch
    {
        1 => Team1,
        2 => Team2,
        _ => Colors.White,
    };
}
