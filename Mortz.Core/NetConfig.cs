namespace Mortz.Core;

/// <summary>Networking constants shared by client and server.</summary>
public static class NetConfig
{
    /// <summary>Bumped on semantic wire changes. Message shape changes are caught
    /// automatically by NetRegistry.SCHEMA_HASH; both ride in Hello.</summary>
    public const int PROTOCOL_VERSION = 24;

    public const int DEFAULT_PORT = 7777;
    public const int MAX_PLAYERS = 8;
    public const int MAX_NAME_LENGTH = 24;

    /// <summary>
    /// A snapshot is broadcast every N simulation ticks. 30 Hz halves both
    /// payload and per-packet framing cost vs every tick; interpolation hides
    /// the gap. Real-session data: 60 Hz uncompressed burned ~10 MB in 15 min.
    /// </summary>
    public const int TICKS_PER_SNAPSHOT = 2;

    /// <summary>
    /// How far in the past remote entities are rendered, in ticks (~67 ms =
    /// 2 snapshot intervals of loss/jitter margin). If real-internet tests
    /// show remote hitching on jittery links, raise this or make it adaptive.
    /// </summary>
    public const int INTERPOLATION_DELAY_TICKS = 4;

    /// <summary>
    /// The client sends an input packet every N simulation ticks. Redundancy
    /// keeps every tick's input on the wire; batching only cuts the number of
    /// packets, which is what per-packet framing overhead is billed on.
    /// </summary>
    public const int TICKS_PER_INPUT_PACKET = 2;

    /// <summary>
    /// How many of the newest inputs each input packet re-sends. At 4 with
    /// packets every 2 ticks, one lost packet costs nothing.
    /// </summary>
    public const int INPUT_REDUNDANCY = 4;
}
