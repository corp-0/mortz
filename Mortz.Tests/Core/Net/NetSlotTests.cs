using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class NetSlotTests
{
    [Fact]
    public void TheAllocatedRangeIsAccepted()
    {
        Assert.Equal(1, new NetSlot(1).Value);
        Assert.Equal(NetConfig.MAX_PLAYERS, new NetSlot(NetConfig.MAX_PLAYERS).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NetConfig.MAX_PLAYERS + 1)]
    [InlineData(255)]
    public void SlotsOutsideTheAllocatedRangeAreRejected(byte value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetSlot(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NetConfig.MAX_PLAYERS + 1)]
    public void TryFromFailsOutsideTheRange(byte value)
    {
        Assert.False(NetSlot.TryFrom(value, out NetSlot slot));
        Assert.Equal(default, slot);
    }

    [Fact]
    public void TryFromAcceptsTheRange()
    {
        Assert.True(NetSlot.TryFrom(3, out NetSlot slot));
        Assert.Equal(3, slot.Value);
        Assert.Equal("3", slot.ToString());
    }
}
