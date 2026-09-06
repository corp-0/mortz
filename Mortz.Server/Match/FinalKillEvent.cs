using Mortz.Core.Sim;

namespace Mortz.Server.Match;

public readonly record struct FinalKillEvent(int Tick, ScoredKill Kill, Death Death, Explosion? Explosion)
{
    public static FinalKillEvent Capture(int tick, ScoredKill kill, Death death,
        IReadOnlyList<Explosion> explosions)
    {
        foreach (Explosion explosion in explosions)
        {
            if (death.ShellId >= 0 && explosion.ShellId == death.ShellId)
                return new FinalKillEvent(tick, kill, death, explosion);
        }
        return new FinalKillEvent(tick, kill, death, null);
    }
}
