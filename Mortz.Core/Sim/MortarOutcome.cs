namespace Mortz.Core.Sim;

public enum MortarOutcome : byte
{
    Flying = 0,
    /// <summary>Hit terrain or reached its in-play lifetime; Position is the
    /// authoritative detonation point.</summary>
    Exploded = 1,
}
