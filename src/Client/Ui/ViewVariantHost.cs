using Godot;

namespace Mortz.Client.Ui;

/// <summary>
/// Hosts exactly one child scene chosen by a state-derived key. Subclasses say
/// what the key currently is and which scene renders each key; they call
/// Refresh from OnResolved and from every watched state event. The child swaps
/// only when the key actually changed, so value updates inside one variant
/// never rebuild it. Variant scenes stay dumb: they bind to state services
/// themselves and can assume they only exist while their mode is active.
/// </summary>
public abstract partial class ViewVariantHost<TKey> : Control where TKey : struct
{
    private TKey _activeKey;
    private Node? _activeView;

    protected abstract TKey CurrentKey();
    protected abstract PackedScene SceneFor(TKey key);

    // The old view detaches immediately (exit-tree unsubscribes it and frees
    // its name for a same-frame replacement) but dies via QueueFree: the swap
    // usually runs from a state event whose invocation list still holds the
    // old view's own handler, which must land on a live object.
    protected void Refresh()
    {
        TKey key = CurrentKey();
        if (_activeView != null && EqualityComparer<TKey>.Default.Equals(_activeKey, key))
            return;
        if (_activeView is { } previous)
        {
            RemoveChild(previous);
            previous.QueueFree();
        }
        _activeKey = key;
        Node view = SceneFor(key).Instantiate();
        if (view is Control control)
            control.SetAnchorsPreset(LayoutPreset.FullRect);
        _activeView = view;
        AddChild(view);
    }
}
