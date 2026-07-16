using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;

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
    public static readonly MatchConfig ProductionConfig = new();
    /// <summary>For tests that shoot the moment they spawn, which spawn protection would block.</summary>
    public static readonly MatchConfig NoSpawnProtectionConfig = new() { SpawnImmunity = 0 };
    public static readonly PlayerStats Stats = PlayerStats.Resolve(ProductionConfig);

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
