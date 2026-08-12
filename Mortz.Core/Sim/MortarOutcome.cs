namespace Mortz.Core.Sim;

public enum MortarOutcome : byte
{
    FLYING = 0,
    /// <summary>Stopped flying; the shell position is its detonation point.</summary>
    EXPLODED = 1,
}
