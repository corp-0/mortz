using Mortz.Core.Match.Events;

namespace Mortz.Client.Announcements;

public readonly record struct MultiKillWording(string Link, string Loud, int Heat);

public readonly record struct StreakWording(
    string Weak,
    string Strong,
    string StreakName,
    string StreakVerb,
    int Heat
);

/// <summary>Wording ladders and pools shared by the banner and chat.</summary>
public static class Vocab
{
    public static MultiKillWording MultiKillTier(byte magnitude) => magnitude switch
    {
        <= 2 => new("just got a", "DOUBLE KILL", 0),
        3 => new("racks up a", "TRIPLE KILL", 1),
        4 => new("goes for", "OVERKILL", 2),
        5 => new("is on an", "ULTRA KILL", 3),
        6 => new("unleashes a", "MASSACRE", 4),
        _ => new("is pure", "CARNAGE", 5),
    };

    /// <summary>One tier per announced streak; the cadence itself lives in
    /// Streaks.AnnouncementOrdinal.</summary>
    public static StreakWording StreakTier(byte magnitude) =>
        Streaks.AnnouncementOrdinal(magnitude) switch
        {
            0 => new("has a", "taste for it", "BLOODLUST", "has", 0),
            1 => new("demands", "obedience", "PUNISHMENT", "serves", 1),
            2 => new("is", "dominating", "DOMINATING", "is", 2),
            3 => new("is a", "MACHINE GOD", "MACHINE GOD", "is a", 3),
            _ => new("is a", "fucking psycho", "FUCKING PSYCHO", "is a", 4),
        };

    private static readonly string[] _blastQuips =
    [
        "couldn't take it anymore",
        "chose the easy way out",
        "went out with a bang"
    ];

    private static readonly string[] _fallQuips =
    [
        "thought they could fly",
        "found the exit",
        "left without saying goodbye"
    ];

    /// <summary>What follows the name in chat's suicide mockery.</summary>
    public static string SuicideQuip(SuicideCause cause, VariantPicker picker)
    {
        string[] pool = cause == SuicideCause.FALL ? _fallQuips : _blastQuips;
        return pool[picker.Next(cause, pool.Length)];
    }
}
