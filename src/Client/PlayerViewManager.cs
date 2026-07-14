using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;

namespace Mortz.Client;

/// <summary>
/// Pool of PlayerView instances, one per visible player. GameView pushes
/// placements between BeginFrame and Prune; anyone not placed this frame
/// despawns. Nameplates come from the server's roster broadcasts, which cover
/// match start and every later join/leave. F3 toggles the sim collision box
/// outlines.
/// </summary>
public partial class PlayerViewManager : Node2D
{
    [Export] private PackedScene _playerScene = null!;

    /// <summary>A remote player's rendered feet position this frame (lag probe tap).</summary>
    public event Action<Vector2>? RemotePlaced;

    private readonly Dictionary<int, PlayerView> _views = new();
    private readonly HashSet<int> _placed = new();
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<int, byte> _skins = new();

    // Everyone renders with the base stats until perks make them per-player.
    private PlayerStats _stats = null!;

    /// <summary>Must be called before the first Place (GameView.Initialize does).</summary>
    public void Configure(PlayerStats stats) => _stats = stats;

    public override void _Ready() => RosterMsg.Received += OnRoster;

    public override void _ExitTree() => RosterMsg.Received -= OnRoster;

    // Views and rosters race (a view can spawn before the first roster, and
    // rosters keep coming as players join/leave), so names apply in both
    // directions: at spawn from the dict, and to live views on every roster.
    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        _skins.Clear();
        int count = Math.Min(msg.PeerIds.Length, Math.Min(msg.Names.Length, msg.Skins.Length));
        for (int i = 0; i < count; i++)
        {
            _names[(int)msg.PeerIds[i]] = msg.Names[i];
            _skins[(int)msg.PeerIds[i]] = msg.Skins[i];
        }
        foreach ((int peerId, PlayerView view) in _views)
            view.SetPlayerName(_names.GetValueOrDefault(peerId, ""));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_sim_boxes"))
            PlayerView.DrawSimBoxes = !PlayerView.DrawSimBoxes;
    }

    public void BeginFrame() => _placed.Clear();

    public void Place(int peerId, Vector2 feet, byte aim, byte skin, byte ammo, byte reloadTicks,
        byte health, byte respawnTicks, byte parryTicks, byte dashCooldown)
    {
        _placed.Add(peerId);
        bool isLocal = peerId == Multiplayer.GetUniqueId();
        if (_skins.TryGetValue(peerId, out byte rosterSkin))
            skin = rosterSkin;
        if (!isLocal)
            RemotePlaced?.Invoke(feet);
        if (!_views.TryGetValue(peerId, out PlayerView? view))
        {
            view = _playerScene.Instantiate<PlayerView>();
            view.Configure(_stats);
            view.SetIsLocal(isLocal);
            view.SetPlayerName(_names.GetValueOrDefault(peerId, ""));
            AddChild(view);
            _views[peerId] = view;
        }
        view.Apply(feet, aim, skin, ammo, reloadTicks, health, respawnTicks, parryTicks, dashCooldown);
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
