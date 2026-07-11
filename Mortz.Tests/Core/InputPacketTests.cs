using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class InputPacketTests
{
    [Fact]
    public void RoundTrips_ConsecutiveInputs()
    {
        InputHistory history = new InputHistory();
        history.Add(10, new PlayerInput(InputButtons.Left));
        history.Add(11, new PlayerInput(InputButtons.Left | InputButtons.Jump));
        history.Add(12, new PlayerInput(InputButtons.Right));

        List<(int Seq, PlayerInput Input)> decoded = InputPacket.Decode(InputPacket.Encode(history.Newest(4)));

        Assert.Equal([
            (10, new PlayerInput(InputButtons.Left)),
            (11, new PlayerInput(InputButtons.Left | InputButtons.Jump)),
            (12, new PlayerInput(InputButtons.Right)),
        ], decoded);
    }

    [Fact]
    public void EmptyPacket_DecodesToNothing()
    {
        Assert.Empty(InputPacket.Decode(InputPacket.Encode([])));
    }
}
