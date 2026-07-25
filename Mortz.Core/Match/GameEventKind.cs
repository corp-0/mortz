namespace Mortz.Core.Match;

/// <summary>One announcer-worthy judgment about play.</summary>
public enum GameEventKind : byte
{
    FIRST_BLOOD = 0,
    /// <summary>A parried shell killed its own shooter.</summary>
    HUMILIATION = 1,
    /// <summary>The victim died carrying a big killstreak.</summary>
    SHUTDOWN = 2,
    /// <summary>One shell, several kills. Magnitude is the body count.</summary>
    HOLY_SHIT = 3,
    /// <summary>Consecutive kills in a short window. Magnitude is the chain.</summary>
    MULTI_KILL = 4,
    /// <summary>Kills since last death crossed a tier. Magnitude is the streak.</summary>
    KILL_STREAK = 5,
}
