namespace Mortz.Client.Announcements;

/// <summary>Chat reads "{name} {Weak} {Strong}"; the banner reads
/// "{name} {StreakVerb} {STREAKNAME}!". Heat ranks the tier from 0.</summary>
public readonly record struct StreakWording(
    string Weak,
    string Strong,
    string StreakName,
    string StreakVerb,
    int Heat
);
