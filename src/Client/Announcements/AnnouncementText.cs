using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Announcements;

public static class AnnouncementText
{
    public static string? Format(GameEventMsg e, Func<long, string> name) => e.Kind switch
    {
        GameEventKind.FIRST_BLOOD => "FIRST BLOOD!",
        GameEventKind.HUMILIATION => $"{name(e.VictimId)} got OWNED",
        GameEventKind.SHUTDOWN => $"{name(e.ActorId)} shut down {name(e.VictimId)}",
        GameEventKind.HOLY_SHIT => "HOLY SHIT!",
        GameEventKind.MULTI_KILL => MultiKill(e.Magnitude),
        GameEventKind.KILL_STREAK => $"{name(e.ActorId)} is on a {StreakName(e.Magnitude)}!",
        _ => null,
    };

    public static string MatchPoint(MatchPointMsg msg) => msg.Remaining <= 1
        ? "ONE MORE KILL!!"
        : $"{msg.Remaining} MORE KILLS!!";

    private static string MultiKill(byte magnitude) => magnitude switch
    {
        2 => "DOUBLE KILL!",
        3 => "TRIPLE KILL!",
        _ => $"MULTI KILL x{magnitude}!",
    };

    private static string StreakName(byte streak) => streak switch
    {
        < 10 => "KILLING SPREE",
        < 15 => "RAMPAGE",
        < 20 => "BLOODBATH",
        _ => "MASSACRE",
    };
}
