namespace Mortz.Core;

/// <summary>
/// Wire format for client -> server input: the newest few inputs of the
/// history, re-sent redundantly each packet so single packet loss costs
/// nothing. Sequences are consecutive, so only the newest one is written.
/// </summary>
public static class InputPacket
{
    private const int HEADER_BYTES = sizeof(int) + sizeof(byte);
    private const int BYTES_PER_INPUT = sizeof(ushort) + sizeof(byte);
    private const InputButtons DEFINED_BUTTONS = InputButtons.Left | InputButtons.Right |
        InputButtons.Jump | InputButtons.Dash | InputButtons.Rope | InputButtons.Up |
        InputButtons.Down | InputButtons.Fire | InputButtons.Reload | InputButtons.Parry;

    public static byte[] Encode(IReadOnlyList<(int Seq, PlayerInput Input)> inputs)
    {
        if (inputs.Count == 0)
            return [];
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter w = new BinaryWriter(ms);
        w.Write(inputs[^1].Seq);
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
        if (data.Length < HEADER_BYTES)
            return false;

        int newestSeq = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);
        int count = data[sizeof(int)];
        if (count is < 1 or > NetConfig.INPUT_REDUNDANCY ||
            data.Length != HEADER_BYTES + count * BYTES_PER_INPUT)
            return false;

        var decoded = new List<(int Seq, PlayerInput Input)>(count);
        int offset = HEADER_BYTES;
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
}
