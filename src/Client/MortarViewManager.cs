using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>
/// Pools of mortar shell views. Everyone else's shells render from reliable
/// lifecycle events, local ballistics, and low-rate corrections;
/// the local player's own authoritative copies are hidden because the
/// predicted ones are already on screen (and at present time, not
/// interpolation-delay time).
/// </summary>
public partial class MortarViewManager : Node2D
{
    [Export] private PackedScene _mortarScene = null!;

    private readonly Dictionary<ushort, MortarView> _remote = new();
    private readonly Dictionary<int, MortarView> _predicted = new();
    private readonly Dictionary<long, MortarView> _replay = new();
    private readonly HashSet<ushort> _seenRemote = new();
    private readonly HashSet<int> _seenPredicted = new();
    private readonly HashSet<long> _seenReplay = new();

    public void SyncRemote(IReadOnlyList<RenderMortar> mortars)
    {
        int localId = Multiplayer.GetUniqueId();
        _seenRemote.Clear();
        foreach (RenderMortar m in mortars)
        {
            // Own shells are already on screen as predicted copies, except a
            // deflected one: nobody predicted that trajectory, so the
            // authoritative copy is the only one there is. Same once the
            // prediction is retired and the server still has the shell.
            if (!ShouldRenderAuthoritative(m, localId, _seenPredicted))
                continue;
            _seenRemote.Add(m.Id);
            Place(_remote, m.Id, new Vector2(m.Position.X, m.Position.Y), m.Velocity,
                playFire: m.OwnerId == localId && !m.Deflected);
        }
        Prune(_remote, _seenRemote);
    }

    internal static bool ShouldRenderAuthoritative(in RenderMortar mortar, int localId,
        IReadOnlySet<int> predictedSeqs) =>
        mortar.OwnerId != localId || mortar.Deflected || !predictedSeqs.Contains(mortar.SpawnSeq);

    /// <summary>Own shells, rendered from prediction (keyed by the input seq that fired).</summary>
    public void SyncPredicted(IReadOnlyList<(int SpawnSeq, MortarState Shell)> shells)
    {
        _seenPredicted.Clear();
        foreach ((int seq, MortarState shell) in shells)
        {
            _seenPredicted.Add(seq);
            Place(_predicted, seq, new Vector2(shell.Position.X, shell.Position.Y), shell.Velocity,
                playFire: true);
        }
        Prune(_predicted, _seenPredicted);
    }

    public void BeginReplay()
    {
        Clear(_remote);
        Clear(_predicted);
    }

    internal void SyncReplay(IReadOnlyList<ReplayMortar> mortars)
    {
        _seenReplay.Clear();
        foreach (ReplayMortar mortar in mortars)
        {
            _seenReplay.Add(mortar.Key);
            Place(_replay, mortar.Key, mortar.Position, mortar.Velocity, playFire: false);
        }
        Prune(_replay, _seenReplay);
    }

    public void EndReplay() => Clear(_replay);

    private void Place<TKey>(Dictionary<TKey, MortarView> pool, TKey key, Vector2 position,
        Vec2 velocity, bool playFire)
        where TKey : notnull
    {
        if (!pool.TryGetValue(key, out MortarView? view))
        {
            view = _mortarScene.Instantiate<MortarView>();
            // Position before AddChild: the trail emits in world space
            // (local_coords off), so a shell entering the tree at origin would
            // streak from (0,0) on its first frame.
            view.Position = position;
            view.Rotation = MathF.Atan2(velocity.Y, velocity.X);
            AddChild(view);
            if (playFire)
                Sfx.PlayAt(Sfx.Sounds.MortarFire, position);
            Sfx.PlayAttached(Sfx.Sounds.ShellWhoosh, view);
            pool[key] = view;
            return;
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

    private static void Clear<TKey>(Dictionary<TKey, MortarView> pool)
        where TKey : notnull
    {
        foreach (MortarView view in pool.Values)
        {
            view.Visible = false;
            view.QueueFree();
        }
        pool.Clear();
    }
}
