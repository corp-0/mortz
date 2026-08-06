using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Chat;
using Mortz.Client.Players;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Sim;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Net;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Views;

/// <summary>Pool of PlayerView instances, one per visible player: GameView pushes
/// placements between BeginFrame and Prune, anyone not placed despawns.</summary>
[Meta(typeof(IAutoNode))]
public partial class PlayerViewManager : Node2D,
    IHandle<PlayerModifiersMsg>,
    IHandle<TypingStateMsg>
{
    private static readonly ILogger _log = MortzLog.For("client");

    [Export] private PackedScene _playerScene = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    /// <summary>A remote player's rendered feet position this frame (lag probe tap).</summary>
    public event Action<Vector2>? RemotePlaced;

    private MatchStateKey<ViewSlot> _view;
    private readonly HashSet<int> _placed = new();
    private bool _replayActive;

    // Base stats are the fallback until a player's own modifier list arrives.
    private MatchConfig _config = null!;
    private PlayerStats _stats = null!;

    /// <summary>Must be called before the first Place (GameView.Initialize does).</summary>
    public void Configure(MatchConfig config)
    {
        _config = config;
        _stats = PlayerStats.Resolve(config);
    }

    public override void _Notification(int what) => this.Notify(what);

    private NetRouter? _routed;

    public void OnResolved()
    {
        _view = Players.MatchKeys.Claim<ViewSlot>();
        Players.Changed += OnPlayersChanged;
        Players.PlayerLeft += OnPlayerLeft;
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        if (_routed == null)
            return;
        Players.Changed -= OnPlayersChanged;
        Players.PlayerLeft -= OnPlayerLeft;
        _routed.Remove(this);
        _routed = null;
    }

    public void Handle(in TypingStateMsg msg)
    {
        ViewSlot slot = Players.GetOrCreate(msg.PeerId).State(_view);
        slot.Typing = msg.IsTyping;
        // The local player polls ChatInputGuard in Place instead.
        if (msg.PeerId != Network.LocalPeerId)
            slot.View?.SetTyping(msg.IsTyping);
    }

    public void Handle(in PlayerModifiersMsg msg)
    {
        PlayerStats stats;
        try
        {
            stats = StatsPipeline.Resolve(_config, ModifierWire.Deserialize(msg.Modifiers));
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _log.Error(e, "dropped malformed modifiers for peer {PeerId}", msg.PeerId);
            return;
        }
        ViewSlot slot = Players.GetOrCreate(msg.PeerId).State(_view);
        slot.Stats = stats;
        slot.View?.Configure(stats);
    }

    private void OnPlayersChanged()
    {
        foreach (ClientPlayer player in Players)
        {
            ViewSlot slot = player.State(_view);
            if (slot.View == null)
                continue;
            slot.View.SetPlayerName(player.Name);
            slot.View.SetTeam(player.Team);
        }
    }

    private void OnPlayerLeft(ClientPlayer player)
    {
        ViewSlot slot = player.State(_view);
        if (slot.View == null)
            return;
        slot.View.QueueFree();
        slot.View = null;
    }

    public PlayerView ViewForTest(int peerId) => Players.Find(peerId)!.State(_view).View!;

    public void BeginFrame() => _placed.Clear();

    public void Place(int peerId, PlayerViewState state)
    {
        _placed.Add(peerId);
        bool isLocal = peerId == Network.LocalPeerId;
        ClientPlayer player = Players.GetOrCreate(peerId);
        ViewSlot slot = player.State(_view);
        // Snapshots carry no skin on the slot-id path, so identity is the
        // only source.
        state = state with { Skin = player.Skin };
        if (!isLocal)
            RemotePlaced?.Invoke(state.Feet);
        if (slot.View == null)
        {
            PlayerView view = _playerScene.Instantiate<PlayerView>();
            view.SetSfx(Sfx);
            view.Configure(slot.Stats ?? _stats);
            view.SetIsLocal(isLocal);
            view.SetPlayerName(player.Name);
            view.SetTeam(player.Team);
            if (!isLocal)
                view.SetTyping(slot.Typing);
            AddChild(view);
            slot.View = view;
        }
        if (isLocal)
            slot.View.SetTyping(ChatInputGuard.IsTyping);
        slot.View.Apply(state, playTransitions: !_replayActive);
    }

    public void SetReplayActive(bool active)
    {
        _replayActive = active;
    }

    /// <summary>Despawn every view not placed since BeginFrame.</summary>
    public void Prune()
    {
        foreach (ClientPlayer player in Players)
        {
            ViewSlot slot = player.State(_view);
            if (slot.View == null || _placed.Contains(player.PeerId))
                continue;
            slot.View.QueueFree();
            slot.View = null;
        }
    }
}
