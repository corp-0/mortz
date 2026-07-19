namespace Mortz.Core.Match;

/// <summary>How a [PlayerStat] config property becomes its PlayerStats
/// field: RAW copies the float; TICKS_* multiply seconds by TICK_RATE and
/// cast to the named integer type; COUNT_BYTE stores an int property in a
/// byte field.</summary>
public enum StatConvert : byte
{
    RAW,
    TICKS_INT,
    TICKS_BYTE,
    TICKS_USHORT,
    COUNT_BYTE,
}
