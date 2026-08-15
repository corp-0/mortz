using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Core.Replication;
using Mortz.Core.Sim;

namespace Mortz.Client.Views;

/// <summary>Pool of mortar views driven by immutable presented frames.</summary>
[Meta(typeof(IAutoNode))]
public partial class MortarViewManager : Node2D
{
    [Export] private PackedScene _mortarScene = null!;

    [Dependency] private ISfx Sfx => this.DependOn<ISfx>();

    public override void _Notification(int what) => this.Notify(what);

    private readonly Dictionary<PresentedMortarKey, MortarView> _views = new();
    private readonly HashSet<PresentedMortarKey> _seen = new();
    private MatchRenderMode? _mode;

    public static bool ShouldRenderAuthoritative(in RenderMortar mortar, int localId,
        IReadOnlySet<int> predictedSeqs, IReadOnlySet<int> completedSeqs) =>
        LiveMatchFrameBuilder.ShouldRenderAuthoritative(
            mortar, localId, predictedSeqs, completedSeqs);

    public void Apply(IReadOnlyList<PresentedMortar> mortars, MatchRenderMode mode)
    {
        if (_mode != mode)
        {
            Clear();
            _mode = mode;
        }

        _seen.Clear();
        foreach (PresentedMortar mortar in mortars)
        {
            _seen.Add(mortar.Key);
            bool playFire = mode == MatchRenderMode.LIVE &&
                            mortar.Key.Source == PresentedMortarSource.PREDICTED;
            Place(_views, mortar.Key, mortar.Position, mortar.Velocity, playFire);
        }

        Prune(_views, _seen);
    }

    public void Clear() => Clear(_views);

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
