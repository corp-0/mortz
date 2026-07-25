using Mortz.Core.Input;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Input;

/// <summary>The sim runs the weapon once per consumed input, so whoever sets
/// the input rate sets the fire rate. A client picks its own sequence numbers
/// and can send far more than one per tick, so admission is anchored to the
/// drain: bunched packets get through whole, fabricated sequences are
/// throttled back to honest rate once the burst allowance is spent.</summary>
public class InputFloodTests
{
    private const byte AIM_UP_LEFT = 160; // shells die on the wall, not on the shooter

    private static PlayerInput In(InputButtons b) => new(b, AIM_UP_LEFT);

    private static void FeedTick(SimWorld w, ref int seq, int perTick, InputButtons buttons)
    {
        for (int i = 0; i < perTick; i++)
        {
            w.EnqueueInput(1, seq++, In(buttons));
        }
        w.Step();
    }

    /// <summary>The flood buys at most its burst allowance of extra weapon
    /// time, once. It must not buy a rate, however long the match runs.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void FloodedSequences_BuyNoSustainedReloadRate(int perTick)
    {
        int honest = TicksToRefillMagazine(perTick: 1);
        int flooded = TicksToRefillMagazine(perTick);

        Assert.True(honest > 4 * SimConfig.MORTAR_RELOAD_TICKS, $"sanity: honest took {honest} ticks");
        Assert.True(flooded >= honest - InputQueue.BURST_SEQS,
            $"flooding at {perTick}/tick refilled in {flooded} ticks against an honest {honest}");
    }

    /// <summary>Cycling starve and burst must refill no faster than honest
    /// play beyond the one-time allowance. With phantom starvation consumes
    /// this cycle hit 1.45x sustained.</summary>
    [Fact]
    public void StarveThenBurstCycles_BuyNoSustainedReloadRate()
    {
        int honest = TicksToRefillMagazine(perTick: 1);

        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        int seq = 0;
        for (int shot = 0; shot < SimConfig.MORTAR_MAX_AMMO; shot++)
        {
            FeedTick(w, ref seq, perTick: 1, InputButtons.FIRE);
            FeedTick(w, ref seq, perTick: 1, InputButtons.NONE);
        }

        int ticks = 0;
        while (w.Players[1].Ammo < SimConfig.MORTAR_MAX_AMMO)
        {
            bool starving = ticks % (2 * InputQueue.BURST_SEQS) < InputQueue.BURST_SEQS;
            FeedTick(w, ref seq, starving ? 0 : 2, InputButtons.NONE);
            ticks++;
        }

        Assert.True(ticks >= honest - InputQueue.BURST_SEQS,
            $"starve/burst cycling refilled in {ticks} ticks against an honest {honest}");
    }

    /// <summary>Reload advances per input, not per tick of silence, and the
    /// frozen ticks are repaid in full once the inputs arrive.</summary>
    [Fact]
    public void Starvation_FreezesTheReload_UntilTheInputsArrive()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        int seq = 0;
        FeedTick(w, ref seq, perTick: 1, InputButtons.FIRE);
        FeedTick(w, ref seq, perTick: 1, InputButtons.RELOAD);
        int frozen = w.Players[1].ReloadTicks;
        Assert.True(frozen > 0, "sanity: a reload is in progress");

        for (int t = 0; t < 6; t++)
        {
            w.Step();
        }
        Assert.Equal(frozen, w.Players[1].ReloadTicks);

        for (int s = 0; s < 6; s++)
        {
            w.EnqueueInput(1, seq++, In(InputButtons.NONE));
        }
        for (int t = 0; t < 4; t++)
        {
            w.Step(); // drains 2, 2, 1, 1
        }
        Assert.Equal(frozen - 6, w.Players[1].ReloadTicks);
    }

    /// <summary>A stall that bunches three packets delivers six sequences at
    /// once. Every one must be simulated, and the ack must not run past the
    /// ones that have not: an acked but unsimulated input is a press the
    /// client keeps and the server never had.</summary>
    [Fact]
    public void SixBunchedSequences_AreAllSimulated_AndTheAckNeverRunsAhead()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        int seq = 0;
        for (int t = 0; t < 10; t++)
        {
            FeedTick(w, ref seq, perTick: 1, InputButtons.NONE);
        }

        // Six ticks of silence, then the stalled packets land together, with the
        // click on the oldest of them.
        for (int t = 0; t < 6; t++)
        {
            w.Step();
        }
        for (int s = 10; s <= 15; s++)
        {
            w.EnqueueInput(1, s, In(s == 10 ? InputButtons.FIRE : InputButtons.NONE));
        }
        w.Step();

        Assert.Equal(11, w.Players[1].LastInputSeq); // two consumed, four still to run
        MortarState shell = Assert.Single(w.Mortars);
        Assert.Equal(10, shell.SpawnSeq);

        for (int t = 0; t < 4; t++)
        {
            w.Step();
        }
        Assert.Equal(15, w.Players[1].LastInputSeq);
        Assert.Equal(0, w.PendingInputs(1));
    }

    [Fact]
    public void OneNext_ConsumesAtMostTheCap_HoweverLongTheBacklog()
    {
        InputQueue q = new InputQueue();
        for (int s = 0; s < 200; s++)
        {
            q.Enqueue(s, In(InputButtons.NONE));
        }

        q.Next();

        Assert.True(q.Consumed.Count <= InputQueue.MAX_CONSUMED,
            $"one Next() consumed {q.Consumed.Count} inputs");
    }

    /// <summary>Alternating press/release sequences are one fire edge every two
    /// inputs, each with its own aim. Two consumed per tick means the second can
    /// only be an edge if the first was not, so a tick fires once.</summary>
    [Fact]
    public void FireEdgeFlood_SpawnsOneShellPerTick()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, In(InputButtons.NONE));
        w.Step();

        for (int s = 1; s <= 60; s++)
        {
            w.EnqueueInput(1, s, In(s % 2 == 1 ? InputButtons.FIRE : InputButtons.NONE));
        }
        w.Step();

        Assert.Single(w.Mortars);
    }

    private static int TicksToRefillMagazine(int perTick)
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        int seq = 0;

        // Empty the magazine at honest rate so only the reload phase differs.
        for (int shot = 0; shot < SimConfig.MORTAR_MAX_AMMO; shot++)
        {
            FeedTick(w, ref seq, perTick: 1, InputButtons.FIRE);
            FeedTick(w, ref seq, perTick: 1, InputButtons.NONE);
        }

        int ticks = 0;
        while (w.Players[1].Ammo < SimConfig.MORTAR_MAX_AMMO)
        {
            FeedTick(w, ref seq, perTick, InputButtons.NONE);
            ticks++;
        }

        return ticks;
    }
}
