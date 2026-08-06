namespace Mortz.Core.Match.Events;

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
    /// <summary>The actor killed the player who last killed them.</summary>
    REVENGE = 6,
    /// <summary>The actor personally killed every member of the enemy roster.</summary>
    TEAM_WIPE = 7,
    /// <summary>Death by own hand. Magnitude is the consecutive count within
    /// the judge's window; Detail carries the <see cref="SuicideCause"/>.</summary>
    SUICIDE = 8,
    /// <summary>A credited kill with no higher-priority presentation.</summary>
    REGULAR_KILL = 9,
    /// <summary>The actor killed a teammate.</summary>
    TEAM_KILL = 10,
}
