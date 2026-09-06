using Mortz.Protocol.Net.Match;

namespace Mortz.Client.Match;

public static class MatchEffects
{
    public static bool IsDecisive(ImpactIdentity impact, FinalKillMsg final) =>
        final.Flags.HasFlag(FinalKillFlags.EXPLOSION) &&
        ((impact.ShellId >= 0 && impact.ShellId == final.ShellId && impact.Tick == final.Tick) ||
         (impact.ShellId < 0 && impact.SpawnSeq >= 0 && impact.SpawnSeq == final.SpawnSeq &&
          impact.OwnerId == final.KillerId));
}
