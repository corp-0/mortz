using Godot;

namespace Mortz.Client;

/// <summary>
/// Pool of PlayerView instances, one per visible player. GameView pushes
/// placements between BeginFrame and Prune; anyone not placed this frame
/// despawns. F3 toggles the sim collision box outlines.
/// </summary>
public partial class PlayerViewManager : Node2D
{
    [Export] private PackedScene _playerScene = null!;

    /// <summary>A remote player's rendered feet position this frame (lag probe tap).</summary>
    public event Action<Vector2>? RemotePlaced;

    private readonly Dictionary<int, PlayerView> _views = new();
    private readonly HashSet<int> _placed = new();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { PhysicalKeycode: Key.F3, Pressed: true, Echo: false })
            PlayerView.DrawSimBoxes = !PlayerView.DrawSimBoxes;
    }

    public void BeginFrame() => _placed.Clear();

    public void Place(int peerId, Vector2 feet, byte aim, byte skin, byte ammo, byte reloadTicks,
        byte health, byte respawnTicks)
    {
        _placed.Add(peerId);
        bool isLocal = peerId == Multiplayer.GetUniqueId();
        if (!isLocal)
            RemotePlaced?.Invoke(feet);
        if (!_views.TryGetValue(peerId, out PlayerView? view))
        {
            view = _playerScene.Instantiate<PlayerView>();
            view.SetIsLocal(isLocal);
            AddChild(view);
            _views[peerId] = view;
        }
        view.Apply(feet, aim, skin, ammo, reloadTicks, health, respawnTicks);
    }

    /// <summary>Despawn every view not placed since BeginFrame.</summary>
    public void Prune()
    {
        foreach ((int peerId, PlayerView view) in _views)
        {
            if (_placed.Contains(peerId))
                continue;
            view.QueueFree();
            _views.Remove(peerId);
        }
    }
}
