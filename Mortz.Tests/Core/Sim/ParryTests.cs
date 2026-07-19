using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Sim;

/// <summary>
/// Parry: F raises a short-lived bubble that flips incoming shells straight back.
/// Spawns on the flat world are deterministic: peer 2 lands at x=130, peer 1
/// at x=241, both on the floor, so a shell fired by peer 2 at aim 0 (+X)
/// flies level into peer 1's bubble.
/// </summary>
public class ParryTests
{
    private const int FIRE_TICK = 5;

    /// <summary>Runs the two-player exchange: peer 1 holds parry from FIRE_TICK,
    /// peer 2 fires once at FIRE_TICK, until the exit condition or the tick cap.</summary>
    private static SimWorld RunExchange(Func<SimWorld, bool> done, int maxTicks)
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.AddPlayer(2);
        for (int t = 0; t < maxTicks && !done(w); t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(t >= FIRE_TICK ? InputButtons.PARRY : InputButtons.NONE));
            w.EnqueueInput(2, t, new PlayerInput(t == FIRE_TICK ? InputButtons.FIRE : InputButtons.NONE));
            w.Step();
        }
        return w;
    }

    [Fact]
    public void ActiveBubble_DeflectsShell_AndRefundsCooldown()
    {
        SimWorld w = RunExchange(w => w.Mortars.Count == 1 && w.Mortars[0].Velocity.X < 0, 60);

        // Exact reversal on X (gravity only ever touches Y). The parrier owns
        // the shell now; FiredBy still points at the original shooter.
        Assert.Single(w.Mortars);
        Assert.Equal(-TestWorlds.NoSpawnProtectionConfig.MortarSpeed, w.Mortars[0].Velocity.X);
        Assert.Equal(1, w.Mortars[0].OwnerId);
        Assert.Equal(2, w.Mortars[0].FiredBy);
        Assert.True(w.Mortars[0].Deflected);

        // The deflect refunded the cooldown mid-window; the parrier is unhurt.
        Assert.Equal(0, w.Players[1].ParryCooldown);
        Assert.True(w.Players[1].ParryTicks > 0);
        Assert.Equal(TestWorlds.Stats.MaxHealth, w.Players[1].Health);
    }

    [Fact]
    public void DeflectedShell_FliesBack_AndOwnsTheShooter()
    {
        SimWorld w = RunExchange(w => w.Players[2].RespawnTicks > 0, 120);

        Assert.True(w.Players[2].RespawnTicks > 0);
        Assert.Equal(TestWorlds.Stats.MaxHealth, w.Players[1].Health); // blast stayed out of range

        // Dying to your own parried shell is the OWNED case, and the kill
        // belongs to the parrier.
        (int peerId, _, int killerId, bool owned) = Assert.Single(w.Deaths);
        Assert.Equal(2, peerId);
        Assert.Equal(1, killerId);
        Assert.True(owned);
    }

    [Fact]
    public void WhiffedParry_PaysTheFullCooldown_AndBlocksTheNextPress()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);

        w.EnqueueInput(1, 0, new PlayerInput(InputButtons.PARRY));
        w.Step();
        Assert.Equal(TestWorlds.Stats.ParryWindowTicks, w.Players[1].ParryTicks);
        Assert.Equal(TestWorlds.Stats.ParryCooldownTicks, w.Players[1].ParryCooldown);

        // Nothing to deflect: the window runs out and the cooldown keeps counting.
        int t = 1;
        for (; t <= SimConfig.PARRY_WINDOW_TICKS; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.NONE));
            w.Step();
        }
        Assert.Equal(0, w.Players[1].ParryTicks);
        Assert.Equal(SimConfig.PARRY_COOLDOWN_TICKS - SimConfig.PARRY_WINDOW_TICKS,
            w.Players[1].ParryCooldown);

        // A fresh press during the cooldown raises nothing.
        w.EnqueueInput(1, t, new PlayerInput(InputButtons.PARRY));
        w.Step();
        Assert.Equal(0, w.Players[1].ParryTicks);
    }

    [Fact]
    public void SnapshotWire_CarriesParryFields()
    {
        PlayerState p = new() { PeerId = 7, ParryTicks = 17, ParryCooldown = 1234 };
        MortarState m = new() { Id = 3, OwnerId = 7, Deflected = true };
        Snapshot restored = Snapshot.Deserialize(new Snapshot(5, [p], [m]).Serialize());

        Assert.Equal((byte)17, restored.Players[0].ParryTicks);
        Assert.Equal((ushort)1234, restored.Players[0].ParryCooldown);
        Assert.True(restored.Mortars[0].Deflected);
    }
}
