namespace Mortz.Core;

/// <summary>
/// Client-side snapshot history + interpolation. The client renders the world
/// <see cref="NetConfig.INTERPOLATION_DELAY_TICKS"/> behind the newest snapshot,
/// lerping between the two snapshots that bracket the render tick. Out-of-order
/// arrivals (unreliable transport) are tolerated; stale ones are dropped.
/// </summary>
public sealed class SnapshotBuffer
{
    private readonly List<Snapshot> _snapshots = new();
    private const int MAX_KEPT = 64;

    public int NewestTick => _snapshots.Count > 0 ? _snapshots[^1].Tick : -1;

    public void Add(Snapshot snapshot)
    {
        int i = _snapshots.FindLastIndex(s => s.Tick < snapshot.Tick);
        if (i == _snapshots.Count - 1) _snapshots.Add(snapshot);
        else if (_snapshots[i + 1].Tick == snapshot.Tick) return; // duplicate
        else _snapshots.Insert(i + 1, snapshot);

        if (_snapshots.Count > MAX_KEPT)
            _snapshots.RemoveRange(0, _snapshots.Count - MAX_KEPT);
    }

    /// <summary>
    /// World state at fractional tick (newest - delay + subTickFraction),
    /// interpolated. Returns null until two snapshots exist.
    /// </summary>
    public InterpolatedState? Sample(float renderTick)
    {
        if (_snapshots.Count < 2) return null;

        // Find the pair bracketing renderTick; clamp to the buffered range.
        Snapshot older = _snapshots[0], newer = _snapshots[1];
        for (int i = _snapshots.Count - 1; i >= 1; i--)
        {
            if (_snapshots[i - 1].Tick <= renderTick)
            {
                older = _snapshots[i - 1];
                newer = _snapshots[i];
                break;
            }
        }

        float span = newer.Tick - older.Tick;
        float t = span > 0 ? Math.Clamp((renderTick - older.Tick) / span, 0f, 1f) : 1f;

        List<RenderPlayer> result = new List<RenderPlayer>();
        foreach (PlayerState np in newer.Players)
        {
            Vec2 pos = np.Position;
            foreach (PlayerState op in older.Players)
            {
                if (op.PeerId == np.PeerId)
                {
                    pos = Vec2.Lerp(op.Position, np.Position, t);
                    break;
                }
            }
            // Aim and rope come from the newer snapshot as-is: the anchor is static
            // while attached, and the flying hook is too fast to bother lerping.
            // Ammo/reload/health too: they step at most once per snapshot anyway.
            result.Add(new RenderPlayer(np.PeerId, pos, np.Aim, np.Skin, np.Rope, np.RopePoint,
                np.Ammo, np.ReloadTicks, np.Health, np.RespawnTicks, np.SpawnImmunityTicks,
                np.ParryTicks, np.DashCooldown));
        }

        // Full-snapshot path for tests and recordings; live traffic uses
        // MortarReplicaSet. Shells matched by id like players. New this interval:
        // rendered at the newer position. Gone from the newer snapshot: exploded,
        // drop it (the carve event is the visual).
        List<RenderMortar> mortars = new List<RenderMortar>();
        foreach (MortarState nm in newer.Mortars)
        {
            Vec2 pos = nm.Position;
            foreach (MortarState om in older.Mortars)
            {
                if (om.Id == nm.Id)
                {
                    pos = Vec2.Lerp(om.Position, nm.Position, t);
                    break;
                }
            }
            mortars.Add(new RenderMortar(nm.Id, nm.OwnerId, nm.Deflected, nm.SpawnSeq,
                pos, nm.Velocity));
        }
        return new InterpolatedState(result, mortars);
    }
}

public readonly record struct RenderPlayer(int PeerId, Vec2 Position, byte Aim, byte Skin, RopeMode Rope, Vec2 RopePoint,
    byte Ammo, byte ReloadTicks, byte Health, byte RespawnTicks, byte SpawnImmunityTicks,
    byte ParryTicks, byte DashCooldown);

public readonly record struct RenderMortar(ushort Id, int OwnerId, bool Deflected, int SpawnSeq,
    Vec2 Position, Vec2 Velocity);

public sealed record InterpolatedState(IReadOnlyList<RenderPlayer> Players, IReadOnlyList<RenderMortar> Mortars);
