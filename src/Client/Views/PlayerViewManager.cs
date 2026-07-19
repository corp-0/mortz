using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Net;

namespace Mortz.Client.Views;

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
    private readonly Dictionary<int, byte> _teams = new();
    private bool _replayActive;

    // Replicated per-player modifier lists resolve through the same
    // StatsPipeline the server runs; base stats are the fallback until a
    // player's list arrives.
    private MatchConfig _config = null!;
    private PlayerStats _stats = null!;
    private readonly Dictionary<int, PlayerStats> _playerStats = new();

    /// <summary>Must be called before the first Place (GameView.Initialize does).</summary>
    public void Configure(MatchConfig config)
    {
        _config = config;
        _stats = PlayerStats.Resolve(config);
    }

    public override void _Ready()
    {
        RosterMsg.Received += OnRoster;
        PlayerModifiersMsg.Received += OnPlayerModifiers;
    }

    public override void _ExitTree()
    {
        RosterMsg.Received -= OnRoster;
        PlayerModifiersMsg.Received -= OnPlayerModifiers;
    }

    // Modifiers race views the same way rosters do: they apply at spawn from
    // the dict and to the live view when a broadcast lands.
    private void OnPlayerModifiers(PlayerModifiersMsg msg)
    {
        PlayerStats stats;
        try
        {
            stats = StatsPipeline.Resolve(_config, ModifierWire.Deserialize(msg.Modifiers));
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            GD.PrintErr($"[client] dropped malformed modifiers for peer {msg.PeerId}");
            return;
        }
        _playerStats[(int)msg.PeerId] = stats;
        if (_views.TryGetValue((int)msg.PeerId, out PlayerView? view))
            view.Configure(stats);
    }

    // Views and rosters race (a view can spawn before the first roster, and
    // rosters keep coming as players join/leave), so names apply in both
    // directions: at spawn from the dict, and to live views on every roster.
    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        _skins.Clear();
        _teams.Clear();
        int count = Math.Min(msg.PeerIds.Length, Math.Min(msg.Names.Length, msg.Skins.Length));
        for (int i = 0; i < count; i++)
        {
            _names[(int)msg.PeerIds[i]] = msg.Names[i];
            _skins[(int)msg.PeerIds[i]] = msg.Skins[i];
            if (i < msg.Teams.Length)
                _teams[(int)msg.PeerIds[i]] = msg.Teams[i];
        }
        foreach ((int peerId, PlayerView view) in _views)
        {
            view.SetPlayerName(_names.GetValueOrDefault(peerId, ""));
            view.SetTeam(_teams.GetValueOrDefault(peerId));
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_sim_boxes"))
            PlayerView.DrawSimBoxes = !PlayerView.DrawSimBoxes;
    }

    internal PlayerView ViewForTest(int peerId) => _views[peerId];

    public void BeginFrame() => _placed.Clear();

    public void Place(int peerId, PlayerViewState state)
    {
        _placed.Add(peerId);
        bool isLocal = peerId == NetworkManager.Instance.LocalPeerId;
        if (_skins.TryGetValue(peerId, out byte rosterSkin))
            state = state with { Skin = rosterSkin };
        if (!isLocal)
            RemotePlaced?.Invoke(state.Feet);
        if (!_views.TryGetValue(peerId, out PlayerView? view))
        {
            view = _playerScene.Instantiate<PlayerView>();
            view.Configure(_playerStats.GetValueOrDefault(peerId, _stats));
            view.SetIsLocal(isLocal);
            view.SetReplayActive(_replayActive);
            view.SetPlayerName(_names.GetValueOrDefault(peerId, ""));
            view.SetTeam(_teams.GetValueOrDefault(peerId));
            AddChild(view);
            _views[peerId] = view;
        }
        view.Apply(state, playTransitions: !_replayActive);
    }

    public void SetReplayActive(bool active)
    {
        _replayActive = active;
        foreach (PlayerView view in _views.Values)
        {
            view.SetReplayActive(active);
        }
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
