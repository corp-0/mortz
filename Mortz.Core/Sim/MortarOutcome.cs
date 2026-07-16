namespace Mortz.Core.Sim;

public enum MortarOutcome : byte
{
    FLYING = 0,
    /// <summary>Hit terrain or reached its in-play lifetime; Position is the
    /// authoritative detonation point.</summary>
    EXPLODED = 1,
}
