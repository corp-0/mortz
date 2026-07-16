using Mortz.Core.Net.Messages;

namespace Mortz.Client;

public static class EliminationText
{
    public static string Format(EliminationMsg message, Func<long, string> name) =>
        (message.Flags & EliminationFlags.OWNED) != 0
            ? $"{name(message.KillerId)} OWNED {name(message.VictimId)}"
            : (message.Flags & EliminationFlags.FALL) != 0
                ? $"{name(message.VictimId)} fell out of the world"
                : (message.Flags & EliminationFlags.SUICIDE) != 0
                    ? $"{name(message.VictimId)} blew themselves up"
                    : (message.Flags & EliminationFlags.TEAM_KILL) != 0
                        ? $"{name(message.KillerId)} team-killed {name(message.VictimId)}"
                        : $"{name(message.KillerId)} killed {name(message.VictimId)}";
}
