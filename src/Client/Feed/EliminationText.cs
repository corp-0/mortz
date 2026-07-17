using Mortz.Core.Net.Messages;

namespace Mortz.Client.Feed;

public static class EliminationText
{
    public static string Format(EliminationMsg message, Func<long, string> name)
    {
        if ((message.Flags & EliminationFlags.OWNED) != 0)
            return $"{name(message.KillerId)} OWNED {name(message.VictimId)}";
        if ((message.Flags & EliminationFlags.FALL) != 0)
            return $"{name(message.VictimId)} fell out of the world";
        if ((message.Flags & EliminationFlags.SUICIDE) != 0)
            return $"{name(message.VictimId)} blew themselves up";
        if ((message.Flags & EliminationFlags.TEAM_KILL) != 0)
            return $"{name(message.KillerId)} team-killed {name(message.VictimId)}";
        return $"{name(message.KillerId)} killed {name(message.VictimId)}";
    }
}
