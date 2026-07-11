namespace Mortz.Core;

/// <summary>
/// Wire format for client → server input: the newest few inputs of the
/// history, re-sent redundantly each packet so single packet loss costs
/// nothing. Sequences are consecutive, so only the newest one is written.
/// </summary>
public static class InputPacket
{
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
            w.Write((byte)input.Buttons);
            w.Write(input.Aim);
        }
        return ms.ToArray();
    }

    public static List<(int Seq, PlayerInput Input)> Decode(byte[] data)
    {
        List<(int, PlayerInput)> result = new List<(int, PlayerInput)>();
        if (data.Length == 0)
            return result;
        using MemoryStream ms = new MemoryStream(data);
        using BinaryReader r = new BinaryReader(ms);
        int newestSeq = r.ReadInt32();
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
        {
            int seq = newestSeq - count + 1 + i;
            InputButtons buttons = (InputButtons)r.ReadByte();
            result.Add((seq, new PlayerInput(buttons, r.ReadByte())));
        }
        return result;
    }
}
