using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core;

public class BlastSimTests
{
    // Position is the feet: body AABB spans X +/- 16, Y-32..Y, center at (X, Y-16).
    private static PlayerState PlayerAt(float x, float y) => new() { Position = new Vec2(x, y) };

    private const float CORE = SimConfig.MORTAR_CARVE_RADIUS * SimConfig.BLAST_CORE_FRACTION;
    private const float RIM = SimConfig.MORTAR_CARVE_RADIUS;
    private static readonly MatchConfig _cfg = TestWorlds.NoSpawnProtectionConfig;

    [Fact]
    public void BlastOnBodyCenter_DealsFullDamage()
    {
        PlayerState p = PlayerAt(100, 100);
        Assert.Equal(SimConfig.MORTAR_DAMAGE, BlastSim.Damage(p, new Vec2(100, 84), _cfg));
    }

    [Fact]
    public void BlastAtCoreEdge_StillDealsFullDamage()
    {
        // Distance is measured to the nearest point of the body box, so a blast
        // CORE px right of the right edge sits exactly on the core boundary.
        PlayerState p = PlayerAt(100, 100);
        Assert.Equal(SimConfig.MORTAR_DAMAGE, BlastSim.Damage(p, new Vec2(116 + CORE, 84), _cfg));
    }

    [Fact]
    public void BlastAtRim_DealsEdgeDamage()
    {
        PlayerState p = PlayerAt(100, 100);
        Assert.Equal(SimConfig.BLAST_EDGE_DAMAGE, BlastSim.Damage(p, new Vec2(116 + RIM, 84), _cfg));
    }

    [Fact]
    public void GrazeRing_FallsOffMonotonically()
    {
        PlayerState p = PlayerAt(100, 100);
        float mid = 116 + (CORE + RIM) / 2;
        int nearer = BlastSim.Damage(p, new Vec2(mid - 3, 84), _cfg);
        int at = BlastSim.Damage(p, new Vec2(mid, 84), _cfg);
        int farther = BlastSim.Damage(p, new Vec2(mid + 3, 84), _cfg);

        Assert.InRange(at, SimConfig.BLAST_EDGE_DAMAGE + 1, SimConfig.MORTAR_DAMAGE - 1);
        Assert.True(nearer > at && at > farther, $"expected {nearer} > {at} > {farther}");
    }

    [Fact]
    public void OutsideRadius_NoDamage()
    {
        PlayerState p = PlayerAt(100, 100);
        Assert.Equal(0, BlastSim.Damage(p, new Vec2(116 + RIM + 1, 84), _cfg));
    }
}
