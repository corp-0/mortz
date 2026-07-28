using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Announcements;
using Mortz.Client.Chat;
using Mortz.Client.Feed;
using Mortz.Client.Replay;
using Mortz.Client.Views;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client.Match;

/// <summary>Composition root of the in-game screen, wired in GameView.tscn.</summary>
[Meta(typeof(IAutoNode))]
public partial class GameView : Node2D,
    IProvide<IKillFeed>,
    IProvide<IAnnouncementDirector>,
    IProvide<ClientChat>,
    IProvide<GameMap>
{
    [Export] private PackedScene _gameMapScene = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;
    [Export] private MortarClient _mortarClient = null!;
    [Export] private PlayerStatusHud _hud = null!;
    [Export] private FinalKillReplay _finalKillReplay = null!;
    [Export] private KillFeed _killFeed = null!;
    [Export] private AnnouncementDirector _announcements = null!;
    [Export] private ClientChat _chat = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    IKillFeed IProvide<IKillFeed>.Value() => _killFeed;
    IAnnouncementDirector IProvide<IAnnouncementDirector>.Value() => _announcements;
    ClientChat IProvide<ClientChat>.Value() => _chat;
    GameMap IProvide<GameMap>.Value() => _gameMap;

    public override void _Notification(int what) => this.Notify(what);

    /// <summary>Diagnostics tap: a snapshot was buffered and reconciled.</summary>
    public event Action<Snapshot>? SnapshotApplied;

    private readonly SnapshotInterpolator _interpolator = new();
    private GameMap _gameMap = null!;
    private Physics _config = null!;
    private readonly Dictionary<byte, int> _peersBySlot = new();

    public int NewestSnapshotTick => _interpolator.NewestTick;
    public float RenderTick => _interpolator.RenderTick;

    /// <summary>Must be called right after instantiating, before entering the tree:
    /// it mounts the map the other nodes depend on.</summary>
    public void Initialize(MapPackage map, MatchConfig config,
        TerrainSyncEncoding terrainEncoding, byte[] terrainData)
    {
        _config = config.Physics;
        _gameMap = _gameMapScene.Instantiate<GameMap>();
        _gameMap.Initialize(map, config.Physics, terrainEncoding, terrainData);
        AddChild(_gameMap);
        // Terrain has to draw under the players, mortars and ropes; AddChild
        // would leave it on top.
        MoveChild(_gameMap, 0);
        _localPlayer.Initialize(new Predictor(_gameMap.Mask, config.Physics));
        _mortarClient.Initialize(config.Physics, () => _interpolator.NewestTick);
        // Base stats to start from; the server's per-player modifier lists
        // (PlayerModifiersMsg) take over as they arrive.
        _players.Configure(config.Physics);
        _hud.Configure(PlayerStats.Resolve(config.Physics));
    }

    public void OnResolved()
    {
        Network.SnapshotReceived += OnSnapshotReceived;
        RosterMsg.Received += OnRoster;
        PlayerModifiersMsg.Received += OnPlayerModifiers;
        this.Provide();
    }

    public void OnExitTree()
    {
        Network.SnapshotReceived -= OnSnapshotReceived;
        RosterMsg.Received -= OnRoster;
        PlayerModifiersMsg.Received -= OnPlayerModifiers;
    }

    private void OnSnapshotReceived(byte[] data, int ack)
    {
        Snapshot snapshot;
        try
        {
            snapshot = Snapshot.Deserialize(data, _peersBySlot);
        }
        catch (InvalidDataException)
        {
            return; // reliable roster for a new slot has not arrived yet
        }
        _interpolator.Add(snapshot);
        _localPlayer.Reconcile(snapshot, ack);
        SnapshotApplied?.Invoke(snapshot);
    }

    // PlayerViewManager owns everyone's view stats; our own modifier list
    // additionally drives prediction and the HUD.
    private void OnPlayerModifiers(PlayerModifiersMsg msg)
    {
        if (msg.PeerId != Network.LocalPeerId)
            return;
        List<StatsModifier> modifiers;
        try
        {
            modifiers = ModifierWire.Deserialize(msg.Modifiers);
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            GD.PrintErr("[client] dropped malformed local player modifiers");
            return;
        }
        _hud.Configure(StatsPipeline.Resolve(_config, modifiers));
        _localPlayer.SetModifiers(modifiers);
    }

    private void OnRoster(RosterMsg msg)
    {
        _peersBySlot.Clear();
        int count = Math.Min(msg.PeerIds.Length, msg.Slots.Length);
        for (int i = 0; i < count; i++)
        {
            if (msg.Slots[i] is > 0 and <= NetConfig.MAX_PLAYERS)
                _peersBySlot[msg.Slots[i]] = (int)msg.PeerIds[i];
        }
    }

    public override void _Process(double delta)
    {
        if (_finalKillReplay.ConsumeFrame((float)delta))
            return;

        // Predicted destruction: our shells carve the instant they land.
        foreach ((int seq, Vec2 pos) in _localPlayer.DrainImpacts())
        {
            _gameMap.PredictCarve(seq, new Vector2(pos.X, pos.Y));
        }

        InterpolatedState? state = _interpolator.Advance((float)delta);
        if (state == null)
            return;

        int localId = Network.LocalPeerId;
        _ropes.Segments.Clear();
        _players.BeginFrame();
        List<ReplayPlayer> replayPlayers = [];

        foreach (RenderPlayer player in state.Players)
        {
            if (player.PeerId == localId)
                continue;
            PlayerViewState viewState = new(
                new Vector2(player.Position.X, player.Position.Y), player.Aim, player.Skin,
                player.Ammo, player.ReloadTicks, player.Health, player.RespawnTicks,
                player.ParryTicks, player.DashCooldown, player.SpawnImmunityTicks);
            _players.Place(player.PeerId, viewState);
            replayPlayers.Add(new ReplayPlayer(player.PeerId, viewState));
            if (player.Rope != RopeMode.NONE)
                _ropes.Segments.Add((BodyCenter(player.Position),
                    new Vector2(player.RopePoint.X, player.RopePoint.Y)));
        }

        if (_localPlayer.Initialized)
        {
            PlayerState local = _localPlayer.State;
            Vector2 feet = new Vector2(local.Position.X, local.Position.Y) + _localPlayer.CorrectionOffset;
            PlayerViewState viewState = new(
                feet, _localPlayer.Aim, local.Skin, local.Ammo, local.ReloadTicks,
                local.Health, local.RespawnTicks, local.ParryTicks, local.DashCooldown,
                local.SpawnImmunityTicks);
            _players.Place(localId, viewState);
            replayPlayers.Add(new ReplayPlayer(localId, viewState));
            _hud.UpdateFrom(local);
            if (local.Rope != RopeMode.NONE)
                _ropes.Segments.Add((BodyCenter(local.Position) + _localPlayer.CorrectionOffset,
                    new Vector2(local.RopePoint.X, local.RopePoint.Y)));
        }

        _players.Prune();
        IReadOnlyList<RenderMortar> remoteMortars = _mortarClient.RenderFrame();

        _finalKillReplay.Record(
            RenderTick,
            replayPlayers,
            remoteMortars,
            _localPlayer.Shells,
            _ropes.Segments,
            localId);
    }

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
