using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>
/// Pools of mortar shell views. Everyone else's shells render from snapshots;
/// the local player's own authoritative copies are hidden because the
/// predicted ones are already on screen (and at present time, not
/// interpolation-delay time).
/// </summary>
public partial class MortarViewManager : Node2D
{
    [Export] private PackedScene _mortarScene = null!;

    private readonly Dictionary<ushort, MortarView> _remote = new();
    private readonly Dictionary<int, MortarView> _predicted = new();
    private readonly HashSet<ushort> _seenRemote = new();
    private readonly HashSet<int> _seenPredicted = new();

    public void SyncRemote(IReadOnlyList<RenderMortar> mortars)
    {
        int localId = Multiplayer.GetUniqueId();
        _seenRemote.Clear();
        foreach (RenderMortar m in mortars)
        {
            if (m.OwnerId == localId)
                continue;
            _seenRemote.Add(m.Id);
            Place(_remote, m.Id, new Vector2(m.Position.X, m.Position.Y), m.Velocity);
        }
        Prune(_remote, _seenRemote);
    }

    /// <summary>Own shells, rendered from prediction (keyed by the input seq that fired).</summary>
    public void SyncPredicted(IReadOnlyList<(int SpawnSeq, MortarState Shell)> shells)
    {
        _seenPredicted.Clear();
        foreach ((int seq, MortarState shell) in shells)
        {
            _seenPredicted.Add(seq);
            Place(_predicted, seq, new Vector2(shell.Position.X, shell.Position.Y), shell.Velocity);
        }
        Prune(_predicted, _seenPredicted);
    }

    private void Place<TKey>(Dictionary<TKey, MortarView> pool, TKey key, Vector2 position, Vec2 velocity)
        where TKey : notnull
    {
        if (!pool.TryGetValue(key, out MortarView? view))
        {
            view = _mortarScene.Instantiate<MortarView>();
            AddChild(view);
            pool[key] = view;
        }
        view.Position = position;
        view.Rotation = MathF.Atan2(velocity.Y, velocity.X);
    }

    private static void Prune<TKey>(Dictionary<TKey, MortarView> pool, HashSet<TKey> seen)
        where TKey : notnull
    {
        foreach ((TKey key, MortarView view) in pool)
        {
            if (seen.Contains(key))
                continue;
            view.QueueFree();
            pool.Remove(key);
        }
    }
}
