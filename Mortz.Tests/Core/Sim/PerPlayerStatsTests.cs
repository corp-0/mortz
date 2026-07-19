using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Core.Sim;

/// <summary>
/// Two players in one world with different modifier stacks actually diverge
/// in behavior: the sim reads each player's own composed stats, not a shared
/// base. Also pins the stack's core guarantee: removing a modifier restores
/// exactly the pre-add stats.
/// </summary>
public class PerPlayerStatsTests
{
    private const int FIRE_TICK = 5;

    [Fact]
    public void FasterModifier_MovesOnlyThatPlayerFarther()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.ProductionConfig);
        w.AddPlayer(1);
        w.AddPlayer(2);
        w.AddModifier(2, new StatsModifier(ModifierId.SPECIAL,
            Mul(Stat.MAX_RUN_SPEED, 2f), Mul(Stat.GROUND_ACCEL, 2f)));
        float start1 = w.Players[1].Position.X;
        float start2 = w.Players[2].Position.X;

        for (int t = 0; t < 15; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.RIGHT));
            w.EnqueueInput(2, t, new PlayerInput(InputButtons.RIGHT));
            w.Step();
        }

        float run1 = w.Players[1].Position.X - start1;
        float run2 = w.Players[2].Position.X - start2;
        Assert.True(run1 > 0);
        Assert.True(run2 > run1 * 1.3f,
            $"modified player ran {run2} px, unmodified {run1} px");
    }

    /// <summary>A level shot flies through the body center, so any radius
    /// deflects it eventually; what the radius decides is how far out. The
    /// bigger bubble must flip the shell measurably farther from the body.</summary>
    [Fact]
    public void ParryRadiusModifier_DeflectsFartherOut()
    {
        float near = DeflectDistance(null);
        float far = DeflectDistance(new StatsModifier(ModifierId.WATER,
            Mul(Stat.PARRY_RADIUS, 4f)));
        Assert.True(far > near + 30,
            $"quadrupled radius deflected at {far} px, base radius at {near} px");
    }

    [Fact]
    public void RemovingAModifier_RestoresExactlyThePreAddStats()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.ProductionConfig);
        w.AddPlayer(1);
        PlayerStats before = w.Stats[1];

        w.AddModifier(1, new StatsModifier(ModifierId.ICE,
            Mul(Stat.GROUND_FRICTION, 0.2f), Add(Stat.MAX_RUN_SPEED, 100f)));
        Assert.NotEqual(before.GroundFriction, w.Stats[1].GroundFriction);
        Assert.NotEqual(before.MaxRunSpeed, w.Stats[1].MaxRunSpeed);

        w.RemoveModifier(1, ModifierId.ICE);
        Assert.Equal(before.GroundFriction, w.Stats[1].GroundFriction);
        Assert.Equal(before.MaxRunSpeed, w.Stats[1].MaxRunSpeed);
        Assert.Equal(before.ParryRadius, w.Stats[1].ParryRadius);
    }

    /// <summary>Same exchange as ParryTests (peer 2 fires level into peer 1's
    /// raised bubble), optionally with a modifier on the parrier. Returns the
    /// shell's horizontal distance from the parrier when it flipped.</summary>
    private static float DeflectDistance(StatsModifier? parrierModifier)
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.AddPlayer(2);
        if (parrierModifier != null)
            w.AddModifier(1, parrierModifier);

        for (int t = 0; t < 120; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(t >= FIRE_TICK ? InputButtons.PARRY : InputButtons.NONE));
            w.EnqueueInput(2, t, new PlayerInput(t == FIRE_TICK ? InputButtons.FIRE : InputButtons.NONE));
            w.Step();
            if (w.Mortars.Count == 1 && w.Mortars[0].Deflected)
                return w.Players[1].Position.X - w.Mortars[0].Position.X;
        }
        Assert.Fail("the shell never deflected");
        return 0;
    }
}
