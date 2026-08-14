using Mortz.Core.Sim;

namespace Mortz.Server.Match;

/// <summary>Records terrain damage for late-join synchronization.</summary>
public class TerrainStep(TerrainHistory history) : IMatchStep
{
    public TerrainHistory History { get; } = history;

    public void Advance(MatchTick tick)
    {
        foreach (Explosion explosion in tick.Explosions)
        {
            History.Record(explosion.X, explosion.Y, explosion.Radius);
        }
    }
}
