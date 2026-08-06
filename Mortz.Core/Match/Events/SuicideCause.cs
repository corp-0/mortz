namespace Mortz.Core.Match.Events;

/// <summary>How a player died by their own hand. Rides in
/// GameEventMsg.Detail.</summary>
public enum SuicideCause : byte
{
    /// <summary>Own shell's blast.</summary>
    BLAST = 0,
    /// <summary>Fell out of the map.</summary>
    FALL = 1,
}
