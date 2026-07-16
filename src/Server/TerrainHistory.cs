using Mortz.Core;
using Mortz.Core.Terrain;

namespace Mortz.Server;

/// <summary>Late-join terrain state for one match. Once the bounded carve log
/// fills, the authoritative bitmap remains the exact fallback.</summary>
internal sealed class TerrainHistory
{
    private readonly List<TerrainCarve> _carves = new();
    private bool _logComplete = true;

    public int CarveCount => _carves.Count;

    public void Record(int x, int y, int radius)
    {
        if (_carves.Count >= TerrainSync.MAX_CARVES)
        {
            _logComplete = false;
            return;
        }
        _carves.Add(new TerrainCarve(
            (short)Math.Clamp(x, short.MinValue, short.MaxValue),
            (short)Math.Clamp(y, short.MinValue, short.MaxValue),
            (byte)Math.Clamp(radius, 0, byte.MaxValue)));
    }

    public TerrainSyncPayload Build(TerrainMask terrain) => _logComplete
        ? TerrainSync.Build(terrain, _carves)
        : new TerrainSyncPayload(TerrainSyncEncoding.RemovedBitmap, terrain.SerializeRemoved());
}
