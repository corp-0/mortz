namespace Mortz.Core.Match.Events;

/// <summary>Streak announcement cadence, shared by the server judge and the
/// client announcer.</summary>
public static class Streaks
{
    /// <summary>Streaks announce at this size, then at every odd streak.</summary>
    public const int ANNOUNCEMENT_ENTRY = 5;

    public static int AnnouncementOrdinal(int streak)
    {
        if (streak < ANNOUNCEMENT_ENTRY || streak % 2 == 0)
            return -1;
        return (streak - ANNOUNCEMENT_ENTRY) / 2;
    }
}
