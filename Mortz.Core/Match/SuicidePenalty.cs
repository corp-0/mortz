namespace Mortz.Core.Match;

/// <summary>What a suicide (own blast or death pit) costs the victim.</summary>
public enum SuicidePenalty : byte
{
    NONE = 0,
    /// <summary>Costs a kill, but never below zero.</summary>
    KILL_NO_NEGATIVE = 1,
    /// <summary>Costs a kill, scores can go negative.</summary>
    KILL = 2,
    REWARD_CLOSEST_ENEMY = 3,
}
