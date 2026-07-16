using Mortz.Core.Net;
using Mortz.Core.Sim;

namespace Mortz.Core.Input;

/// <summary>
/// Wire format for client -> server input: the newest few inputs of the
/// history, re-sent redundantly each packet so single packet loss costs
/// nothing. Sequences are consecutive, so only the newest one is written.
/// </summary>
public static class InputPacket
{
    private const int BYTES_PER_INPUT = sizeof(ushort) + sizeof(byte);
    private const InputButtons DEFINED_BUTTONS = InputButtons.LEFT | InputButtons.RIGHT |
        InputButtons.JUMP | InputButtons.DASH | InputButtons.ROPE | InputButtons.UP |
        InputButtons.DOWN | InputButtons.FIRE | InputButtons.RELOAD | InputButtons.PARRY;

    public static byte[] Encode(IReadOnlyList<(int Seq, PlayerInput Input)> inputs)
    {
        if (inputs.Count == 0)
            return [];
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter w = new BinaryWriter(ms);
        WriteVarUInt(w, unchecked((uint)inputs[^1].Seq));
        w.Write((byte)inputs.Count);
        foreach ((int _, PlayerInput input) in inputs)
        {
            w.Write((ushort)input.Buttons);
            w.Write(input.Aim);
        }
        return ms.ToArray();
    }

    public static List<(int Seq, PlayerInput Input)> Decode(byte[] data)
    {
        TryDecode(data, out List<(int Seq, PlayerInput Input)> result);
        return result;
    }

    /// <summary>
    /// Parses an exact input datagram without throwing. Empty packets, impossible
    /// counts, undefined button flags, truncation and trailing bytes are invalid.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> data,
        out List<(int Seq, PlayerInput Input)> result)
    {
        result = [];
        int offset = 0;
        if (!TryReadVarUInt(data, ref offset, out uint newestRaw) || offset >= data.Length)
            return false;

        int newestSeq = unchecked((int)newestRaw);
        int count = data[offset++];
        if (count is < 1 or > NetConfig.INPUT_REDUNDANCY ||
            data.Length != offset + count * BYTES_PER_INPUT)
            return false;

        List<(int Seq, PlayerInput Input)> decoded = new(count);
        for (int i = 0; i < count; i++)
        {
            InputButtons buttons = (InputButtons)System.Buffers.Binary.BinaryPrimitives
                .ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));
            if ((buttons & ~DEFINED_BUTTONS) != 0)
                return false;
            int seq = unchecked(newestSeq - count + 1 + i);
            decoded.Add((seq, new PlayerInput(buttons, data[offset + sizeof(ushort)])));
            offset += BYTES_PER_INPUT;
        }
        result = decoded;
        return true;
    }

    private static void WriteVarUInt(BinaryWriter w, uint value)
    {
        while (value >= 0x80)
        {
            w.Write((byte)(value | 0x80));
            value >>= 7;
        }
        w.Write((byte)value);
    }

    private static bool TryReadVarUInt(ReadOnlySpan<byte> data, ref int offset, out uint value)
    {
        value = 0;
        int start = offset;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            if (offset >= data.Length)
                return false;
            byte next = data[offset++];
            if (shift == 28 && (next & 0xF0) != 0)
                return false;
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return offset - start == 1 || next != 0; // reject non-canonical overlong forms
        }
        return false;
    }
}
