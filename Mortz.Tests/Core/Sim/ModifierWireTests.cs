using Mortz.Core.Sim.Modifiers;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Core.Sim;

public class ModifierWireTests
{
    [Fact]
    public void RoundTrips_PreservingOrderAndValues()
    {
        byte[] blob = ModifierWire.Serialize(
        [
            new StatsModifier(ModifierId.WATER, Mul(Stat.GRAVITY, 0.4f), Add(Stat.TOTAL_JUMPS, -1f)),
            new StatsModifier(ModifierId.ICE, Mul(Stat.GROUND_FRICTION, 0.2f)),
        ]);
        List<StatsModifier> got = ModifierWire.Deserialize(blob);

        Assert.Equal(2, got.Count);
        Assert.Equal(ModifierId.WATER, got[0].Id);
        Assert.Equal(ModifierId.ICE, got[1].Id);
        Assert.Equal([Mul(Stat.GRAVITY, 0.4f), Add(Stat.TOTAL_JUMPS, -1f)], got[0].Changes);
        Assert.Equal([Mul(Stat.GROUND_FRICTION, 0.2f)], got[1].Changes);
    }

    [Fact]
    public void EmptyList_RoundTrips()
    {
        Assert.Empty(ModifierWire.Deserialize(ModifierWire.Serialize([])));
    }

    [Fact]
    public void TrailingBytes_Throw()
    {
        byte[] valid = ModifierWire.Serialize([Modifiers.Ice]);
        Assert.Throws<InvalidDataException>(() => ModifierWire.Deserialize([.. valid, 0]));
    }

    [Fact]
    public void UnknownStat_Throws()
    {
        // count=1, id=0, changeCount=1, stat=200 (undefined), op=0, value
        byte[] hostile = [1, 0, 1, 200, 0, 0, 0, 0, 0];
        Assert.Throws<InvalidDataException>(() => ModifierWire.Deserialize(hostile));
    }

    [Fact]
    public void TruncatedBytes_Throw()
    {
        byte[] valid = ModifierWire.Serialize([Modifiers.Water]);
        Assert.Throws<EndOfStreamException>(() => ModifierWire.Deserialize(valid[..^1]));
    }
}
