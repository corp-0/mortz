namespace Mortz.Core;

public static class NetAbusePolicy
{
    /// <summary>
    /// Reliable-message buckets charge one token per packet plus one per 4 KiB,
    /// preventing a burst allowance from multiplying into a large byte burst.
    /// </summary>
    public static double EnvelopeCost(int payloadBytes)
    {
        if (payloadBytes < 0)
            return double.PositiveInfinity;
        return 1d + payloadBytes / 4096d;
    }
}
