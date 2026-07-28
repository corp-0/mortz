namespace Mortz.Core.Match;

public sealed class MatchConfig
{
    public ModeRules Rules { get; init; } = new();

    public Physics Physics { get; init; } = new();

    public void Clamp()
    {
        Rules.Clamp();
        Physics.Clamp();
    }

    // Changing this layout requires a protocol version bump.
    public byte[] ToBytes()
    {
        byte[] rules = Rules.ToBytes();
        byte[] physics = Physics.ToBytes();
        byte[] combined = new byte[4 + rules.Length + physics.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(combined, rules.Length);
        rules.CopyTo(combined, 4);
        physics.CopyTo(combined, 4 + rules.Length);
        return combined;
    }

    public static MatchConfig FromBytes(byte[] data)
    {
        if (data.Length < 4)
            throw new System.IO.InvalidDataException("Match configuration too short.");
        int rulesLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);
        if (rulesLength < 0 || rulesLength > data.Length - 4)
            throw new System.IO.InvalidDataException("Match configuration rules length out of range.");
        return new MatchConfig
        {
            Rules = ModeRules.FromBytes(data[4..(4 + rulesLength)]),
            Physics = Physics.FromBytes(data[(4 + rulesLength)..]),
        };
    }
}
