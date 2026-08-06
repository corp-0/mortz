namespace Mortz.Server;

/// <summary>Transport counters since the last read: ENet framing and
/// compression included, IP/UDP headers not.</summary>
public readonly record struct WireStats(
    double SentBytes,
    double RecvBytes,
    double SentPackets,
    double RecvPackets
);
