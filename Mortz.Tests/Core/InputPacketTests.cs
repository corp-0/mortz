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
        Assert.True(InputPacket.TryDecode(InputPacket.Encode(history.Newest(4)), out _));
        Assert.Equal(11, InputPacket.Encode(history.Newest(4)).Length); // 1-byte seq + count + inputs
    }

    [Fact]
    public void EmptyPacket_DecodesToNothing()
    {
        Assert.Empty(InputPacket.Decode(InputPacket.Encode([])));
        Assert.False(InputPacket.TryDecode([], out _));
    }

    [Fact]
    public void TryDecode_RejectsEveryTruncationAndTrailingBytes()
    {
        byte[] valid = InputPacket.Encode([
            (8, new PlayerInput(InputButtons.Left, 1)),
            (9, new PlayerInput(InputButtons.Fire | InputButtons.Parry, 255)),
        ]);
        for (int length = 0; length < valid.Length; length++)
            Assert.False(InputPacket.TryDecode(valid.AsSpan(0, length), out _));
        Assert.False(InputPacket.TryDecode([.. valid, 0xA5], out _));
    }

    [Fact]
    public void TryDecode_RejectsZeroExcessiveCountsAndUndefinedButtons()
    {
        byte[] zeroCount = [0, 0];
        Assert.False(InputPacket.TryDecode(zeroCount, out _));

        byte[] excessive = new byte[2 + (NetConfig.INPUT_REDUNDANCY + 1) * 3];
        excessive[1] = NetConfig.INPUT_REDUNDANCY + 1;
        Assert.False(InputPacket.TryDecode(excessive, out _));

        byte[] undefined = InputPacket.Encode([(1, new PlayerInput(InputButtons.Left))]);
        undefined[2] = 0;
        undefined[3] = 0x80;
        Assert.False(InputPacket.TryDecode(undefined, out _));
    }

    [Fact]
    public void SequenceVarint_RoundTripsFullIntRangeAndRejectsOverflow()
    {
        foreach (int seq in new[] { 0, 127, 128, 16_384, int.MaxValue, int.MinValue, -1 })
        {
            byte[] packet = InputPacket.Encode([(seq, new PlayerInput(InputButtons.Left))]);
            Assert.True(InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> decoded));
            Assert.Equal(seq, decoded[0].Seq);
        }
        Assert.False(InputPacket.TryDecode([0x80, 0x80, 0x80, 0x80, 0x10, 1, 0, 0, 0], out _));
        Assert.False(InputPacket.TryDecode([0x80, 0x00, 1, 1, 0, 0], out _));
    }

    [Fact]
    public void TryDecode_RandomPacketsNeverThrow()
    {
        var random = new Random(912_771);
        for (int i = 0; i < 10_000; i++)
        {
            byte[] data = new byte[random.Next(0, 129)];
            random.NextBytes(data);
            Exception? error = Record.Exception(() => InputPacket.TryDecode(data, out _));
            Assert.Null(error);
        }
    }
}
