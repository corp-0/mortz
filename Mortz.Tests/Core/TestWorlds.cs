using Mortz.Core;

namespace Mortz.Tests.Core;

/// <summary>Synthetic terrain for sim tests: flat solid floor, side walls, open sky.</summary>
public static class TestWorlds
{
    public const int WIDTH = 400;
    public const int HEIGHT = 300;
    public const float FLOOR_Y = 250;
    public const float WALL_LEFT = 8;
    public const float WALL_RIGHT = 392;

    /// <summary>Default ruleset; asserts keep reading expected values off the SimConfig consts.</summary>
    public static readonly MatchConfig Config = new();
    public static readonly PlayerStats Stats = PlayerStats.Resolve(Config);

    public static TerrainMask Flat(
        Func<int, int, bool>? extraSolid = null,
        Func<int, int, bool>? destructible = null)
    {
        return new TerrainMask(WIDTH, HEIGHT,
            solid: (x, y) =>
                y >= FLOOR_Y || x < WALL_LEFT || x >= WALL_RIGHT || (extraSolid?.Invoke(x, y) ?? false),
            destructible: (x, y) => destructible?.Invoke(x, y) ?? false);
    }
}
