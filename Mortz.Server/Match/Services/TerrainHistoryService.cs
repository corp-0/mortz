using Mortz.Core.Sim;

namespace Mortz.Server.Match.Services;

/// <summary>Records terrain damage for late-join synchronization.</summary>
public class TerrainHistoryService(TerrainHistory history) : IObserveMatchUpdate
{
    public void MatchUpdated(in MatchUpdate update, ServerTime time)
    {
        foreach (Explosion explosion in update.Explosions)
        {
            history.Record(explosion.X, explosion.Y, explosion.Radius);
        }
    }
}
