namespace Mortz.Core.Net;

/// <summary>A player's slot in the running match, 1..NetConfig.MAX_PLAYERS.</summary>
public readonly record struct NetSlot
{
    public NetSlot(byte value)
    {
        if (value is 0 or > NetConfig.MAX_PLAYERS)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public byte Value { get; }

    public override string ToString() => Value.ToString();

    public static bool TryFrom(byte value, out NetSlot slot)
    {
        bool valid = value is not 0 && value <= NetConfig.MAX_PLAYERS;
        slot = valid ? new NetSlot(value) : default;
        return valid;
    }
}
