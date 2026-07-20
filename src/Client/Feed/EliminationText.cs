using Mortz.Core.Net.Messages;

namespace Mortz.Client.Feed;

public static class EliminationText
{
    public static string Format(EliminationMsg message, Func<long, string> name)
    {
        if (message.Flags.HasFlag(EliminationFlags.OWNED))
            return $"{name(message.KillerId)} OWNED {name(message.VictimId)}";
        if (message.Flags.HasFlag(EliminationFlags.FALL))
            return $"{name(message.VictimId)} fell out of the world";
        if (message.Flags.HasFlag(EliminationFlags.SUICIDE))
            return $"{name(message.VictimId)} blew themselves up";
        if (message.Flags.HasFlag(EliminationFlags.TEAM_KILL))
            return $"{name(message.KillerId)} team-killed {name(message.VictimId)}";
        return $"{name(message.KillerId)} killed {name(message.VictimId)}";
    }
}
