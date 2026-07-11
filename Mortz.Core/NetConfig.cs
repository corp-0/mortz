namespace Mortz.Core;

/// <summary>Networking constants shared by client and server.</summary>
public static class NetConfig
{
    /// <summary>Bumped on every wire-incompatible change; mismatched clients are rejected.</summary>
    public const int PROTOCOL_VERSION = 7;

    public const int DEFAULT_PORT = 7777;
    public const int MAX_PLAYERS = 8;

    /// <summary>
    /// A snapshot is broadcast every N simulation ticks. Snapshots stay
    /// per-player-state only (terrain and projectiles are events), so every
    /// tick is affordable; revisit if snapshots ever carry entity swarms.
    /// </summary>
    public const int TICKS_PER_SNAPSHOT = 1;

    /// <summary>
    /// How far in the past remote entities are rendered, in ticks (~50 ms =
    /// 3 snapshot intervals of loss/jitter margin). If real-internet tests
    /// show remote hitching on jittery links, raise this or make it adaptive.
    /// </summary>
    public const int INTERPOLATION_DELAY_TICKS = 3;

    /// <summary>How many of the newest inputs each input packet re-sends.</summary>
    public const int INPUT_REDUNDANCY = 4;
}
