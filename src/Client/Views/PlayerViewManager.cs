using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Chat;
using Mortz.Client.Players;
using Mortz.Net;

namespace Mortz.Client.Views;

/// <summary>Pool of PlayerView instances, one per visible player: GameView pushes
/// placements between BeginFrame and Prune, anyone not placed despawns.</summary>
[Meta(typeof(IAutoNode))]
public partial class PlayerViewManager : Node2D
{
    [Export] private PackedScene _playerScene = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    /// <summary>A remote player's rendered feet position this frame (lag probe tap).</summary>
    public event Action<Vector2>? RemotePlaced;

    private readonly Dictionary<int, PlayerView> _views = [];
    private readonly HashSet<int> _placed = new();
    private bool _replayActive;
    private bool _subscribed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Players.Changed += OnPlayersChanged;
        Players.PlayerLeft += OnPlayerLeft;
        Players.MatchStatsChanged += OnMatchStatsChanged;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Players.Changed -= OnPlayersChanged;
        Players.PlayerLeft -= OnPlayerLeft;
        Players.MatchStatsChanged -= OnMatchStatsChanged;
        _subscribed = false;
    }

    private void OnPlayersChanged()
    {
        foreach ((int peerId, PlayerView view) in _views)
        {
            ClientPlayer? player = Players.Find(peerId);
            if (player == null)
                continue;
            view.SetPlayerName(player.Name);
            view.SetTeam(player.Team);
        }
    }

    private void OnPlayerLeft(ClientPlayer player)
    {
        if (!_views.Remove(player.PeerId, out PlayerView? view))
            return;
        view.QueueFree();
    }

    private void OnMatchStatsChanged(ClientPlayer player)
    {
        if (player.Match != null && _views.TryGetValue(player.PeerId, out PlayerView? view))
            view.Configure(player.Match.Stats);
    }

    public PlayerView ViewForTest(int peerId) => _views[peerId];

    public void BeginFrame() => _placed.Clear();

    /// <summary>
    /// Ran every rendering tick for every player
    /// </summary>
    public void Place(int peerId, PlayerViewState state)
    {
        _placed.Add(peerId);
        bool isLocal = peerId == Network.LocalPeerId;
        ClientPlayer player = Players.GetOrCreate(peerId);
        ClientMatchPlayer match = player.Match ??
            throw new InvalidOperationException("Cannot place a player outside a match.");
        // Snapshots carry no skin on the slot-id path, so identity is the
        // only source.
        state = state with { Skin = player.Skin };
        if (!isLocal)
            RemotePlaced?.Invoke(state.Feet);
        if (!_views.TryGetValue(peerId, out PlayerView? view))
        {
            view = _playerScene.Instantiate<PlayerView>();
            view.SetSfx(Sfx);
            view.Configure(match.Stats);
            view.SetIsLocal(isLocal);
            view.SetPlayerName(player.Name);
            view.SetTeam(player.Team);
            AddChild(view);
            _views[peerId] = view;
        }
        view.Apply(state, playTransitions: !_replayActive);
        bool typing = isLocal ? ChatInputGuard.IsTyping : player.IsTyping;
        view.SetTyping(!_replayActive && typing);
    }

    public void SetReplayActive(bool active)
    {
        _replayActive = active;
    }

    /// <summary>Despawn every view not placed since BeginFrame.</summary>
    public void Prune()
    {
        foreach (int peerId in _views.Keys.ToArray())
        {
            if (_placed.Contains(peerId))
                continue;
            _views[peerId].QueueFree();
            _views.Remove(peerId);
        }
    }
}
