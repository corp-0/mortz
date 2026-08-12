using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Terrain;

public class TerrainMaskTests
{
    private static TerrainMask BlobWorld() => TestWorlds.Flat(
        destructible: (x, y) => x is >= 100 and < 200 && y is >= 100 and < 200);

    [Fact]
    public void SameCarves_ProduceIdenticalMasks_Determinism()
    {
        TerrainMask a = BlobWorld();
        TerrainMask b = BlobWorld();
        foreach (TerrainMask m in new[] { a, b })
        {
            m.CarveCircle(150, 150, 30);
            m.CarveCircle(120, 110, 25);
            m.CarveCircle(500, 500, 40); // mostly out of destructible area
        }

        Assert.Equal(a.SerializeRemoved(), b.SerializeRemoved());
        for (int y = 0; y < TestWorlds.HEIGHT; y += 3)
        {
            for (int x = 0; x < TestWorlds.WIDTH; x += 3)
            {
                Assert.Equal(a.Get(x, y), b.Get(x, y));
            }
        }
    }

    [Fact]
    public void Carve_RemovesDestructibleOnly()
    {
        TerrainMask m = BlobWorld();
        // Circle overlapping both the blob and the solid floor below it.
        List<(int X, int Y)> removed = m.CarveCircle(150, 250, 60);

        Assert.NotEmpty(removed);
        Assert.All(removed, p => Assert.Equal(TerrainMaterial.EMPTY, m.Get(p.X, p.Y)));
        Assert.Equal(TerrainMaterial.SOLID, m.Get(150, 255)); // floor survives
        Assert.Equal(TerrainMaterial.DESTRUCTIBLE, m.Get(105, 105)); // outside circle survives
    }

    [Fact]
    public void SolidWall_ShieldsDestructibleTerrainBehindIt()
    {
        TerrainMask m = new(100, 100,
            solid: (x, _) => x == 50,
            destructible: (x, y) => x is >= 30 and <= 70 && y is >= 30 and <= 70);

        m.CarveCircle(40, 50, 20);

        Assert.Equal(TerrainMaterial.EMPTY, m.Get(49, 50));
        Assert.Equal(TerrainMaterial.SOLID, m.Get(50, 50));
        Assert.Equal(TerrainMaterial.DESTRUCTIBLE, m.Get(51, 50));
        Assert.Equal(TerrainMaterial.DESTRUCTIBLE, m.Get(60, 50));
    }

    [Fact]
    public void SurfaceNormal_PointsOutOfFloorsAndWalls()
    {
        TerrainMask floor = new(100, 100,
            solid: (_, y) => y >= 50,
            destructible: (_, _) => false);
        TerrainMask wall = new(100, 100,
            solid: (x, _) => x >= 50,
            destructible: (_, _) => false);

        Assert.Equal(new Vec2(0, -1), floor.SurfaceNormal(40, 50, new Vec2(0, 1)));
        Assert.Equal(new Vec2(-1, 0), wall.SurfaceNormal(50, 40, new Vec2(1, 0)));
    }

    [Fact]
    public void SurfaceNormal_FollowsDiagonalTerrain()
    {
        TerrainMask slope = new(100, 100,
            solid: (x, y) => y >= x,
            destructible: (_, _) => false);

        Vec2 normal = slope.SurfaceNormal(50, 50, new Vec2(-1, 1).Normalized());

        Assert.True(normal.X > 0);
        Assert.True(normal.Y < 0);
        Assert.Equal(1, normal.Length(), 0.001f);
    }

    [Fact]
    public void SurfaceNormal_UsesIncomingDirectionForAnIsolatedPixel()
    {
        TerrainMask isolated = new(100, 100,
            solid: (x, y) => x == 50 && y == 50,
            destructible: (_, _) => false);
        Vec2 incoming = new Vec2(3, 4).Normalized();

        Assert.Equal(-incoming, isolated.SurfaceNormal(50, 50, incoming));
    }

    [Fact]
    public void RemovedMask_RoundTripsOntoPristineCopy()
    {
        TerrainMask a = BlobWorld();
        a.CarveCircle(150, 150, 35);
        a.CarveCircle(180, 120, 20);

        TerrainMask b = BlobWorld();
        int reported = 0;
        b.ApplyRemoved(a.SerializeRemoved(), (_, _) => reported++);

        Assert.True(reported > 0);
        Assert.Equal(a.SerializeRemoved(), b.SerializeRemoved());
        for (int y = 100; y < 200; y++)
        {
            for (int x = 100; x < 200; x++)
            {
                Assert.Equal(a.Get(x, y), b.Get(x, y));
            }
        }
    }

    [Fact]
    public void OutOfBounds_IsEmpty_DeathPitsNeedNoWalls()
    {
        TerrainMask m = TestWorlds.Flat();
        Assert.False(m.IsSolid(-1, 50));
        Assert.False(m.IsSolid(50, -1));
        Assert.False(m.IsSolid(TestWorlds.WIDTH, 50));
        Assert.Equal(TerrainMaterial.EMPTY, m.Get(-5, -5));
    }
}
