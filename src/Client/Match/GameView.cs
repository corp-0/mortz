using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Announcements;
using Mortz.Client.Chat;
#if TOOLS
using Mortz.Client.E2E;
#endif
using Mortz.Client.Replay;
using Mortz.Client.Players;
using Mortz.Client.Spectating;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Net;
using Mortz.Core.Net.Sim;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Shared.Logging;
using Serilog;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client.Match;

/// <summary>Composition root of the in-game screen, wired in GameView.tscn.</summary>
[Meta(typeof(IAutoNode))]
public partial class GameView : Node2D,
    IHandle<PlayerModifiersMsg>,
    IHandle<MatchStartMsg>,
    IProvide<IAnnouncementDirector>,
    IProvide<ClientChat>,
    IProvide<GameMap>
{
    private static readonly ILogger _log = MortzLog.For("client");

    [Export] private PackedScene _gameMapScene = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;
    [Export] private MortarClient _mortarClient = null!;
    [Export] private PlayerStatusHud _hud = null!;
    [Export] private FinalKillReplay _finalKillReplay = null!;
    [Export] private AnnouncementDirector _announcements = null!;
    [Export] private ClientChat _chat = null!;
    [Export] private SpectatorController _spectator = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    IAnnouncementDirector IProvide<IAnnouncementDirector>.Value() => _announcements;
    ClientChat IProvide<ClientChat>.Value() => _chat;
    GameMap IProvide<GameMap>.Value() => _gameMap;

    public override void _Notification(int what) => this.Notify(what);

    /// <summary>Diagnostics tap: a snapshot was buffered and reconciled.</summary>
    public event Action<Snapshot>? SnapshotApplied;

    private readonly SnapshotInterpolator _interpolator = new();
    private GameMap _gameMap = null!;
    private MatchConfig _config = null!;
    private byte[] _initialSnapshot = [];
    private int _initialSnapshotAck;
    private int _generation;

    /// <summary>The map this match is played on; set by Initialize.</summary>
    public string MapId { get; private set; } = "";

    public int NewestSnapshotTick => _interpolator.NewestTick;
    public float RenderTick => _interpolator.RenderTick;

    /// <summary>Must be called right after instantiating, before entering the tree:
    /// it mounts the map the other nodes depend on.</summary>
    public void Initialize(int generation, MapPackage map, MatchConfig config,
        TerrainSyncEncoding terrainEncoding, byte[] terrainData,
        MatchParticipation participation, byte[] initialSnapshot, int initialSnapshotAck)
    {
        _generation = generation;
        _config = config;
        MapId = map.MapId;
        _gameMap = _gameMapScene.Instantiate<GameMap>();
        _gameMap.Initialize(map, config.Combat, terrainEncoding, terrainData);
        _gameMap.SetZonesVisible(PlayerView.DrawSimBoxes);
        AddChild(_gameMap);
        // Terrain has to draw under the players, mortars and ropes; AddChild
        // would leave it on top.
        MoveChild(_gameMap, 0);
        _initialSnapshot = initialSnapshot;
        _initialSnapshotAck = initialSnapshotAck;
        _localPlayer.Initialize(new Predictor(_gameMap.Mask, config, map.Zones), participation);
        _localPlayer.Frozen = true;
        _spectator.Initialize(participation, new Vector2(map.Width / 2f, map.Height / 2f));
        _mortarClient.Initialize(config.Combat, map.Zones, () => _interpolator.NewestTick);
        // Base stats to start from; the server's per-player modifier lists
        // (PlayerModifiersMsg) take over as they arrive.
        _players.Configure(config);
        _hud.Configure(PlayerStats.Resolve(config));
        _hud.Visible = participation.Activity == MatchActivity.ACTIVE;
    }

    public void OnResolved()
    {
        Network.SnapshotReceived += OnSnapshotReceived;
        _routed = Router;
        _routed.Add(this);
        _spectator.ParticipationChanged += OnParticipationChanged;
        this.Provide();
        new PhaseReadyMsg(_generation).SendToServer();
        OnSnapshotReceived(_initialSnapshot, _initialSnapshotAck);
#if TOOLS
        // Built in code, never declared in GameView.tscn: see ClientE2ERoot.
        ClientE2ERoot.AttachMatch(this, _localPlayer);
#endif
    }

    private NetRouter? _routed;

    public void Handle(in MatchStartMsg message)
    {
        if (message.Generation == _generation)
            _localPlayer.Frozen = false;
    }

    public void OnReady()
    {
        Input.MouseMode = Input.MouseModeEnum.Confined;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("toggle_sim_boxes"))
            return;
        PlayerView.DrawSimBoxes = !PlayerView.DrawSimBoxes;
        _gameMap.SetZonesVisible(PlayerView.DrawSimBoxes);
    }

    public void OnExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (_routed == null)
            return;
        Network.SnapshotReceived -= OnSnapshotReceived;
        _spectator.ParticipationChanged -= OnParticipationChanged;
        _routed.Remove(this);
        _routed = null;
    }

    private void OnParticipationChanged(MatchParticipation participation)
    {
        _localPlayer.SetParticipation(participation);
        _hud.Visible = participation.Activity == MatchActivity.ACTIVE;
    }

    private void OnSnapshotReceived(byte[] data, int ack)
    {
        Snapshot snapshot;
        try
        {
            snapshot = Snapshot.Deserialize(data, Players.Table);
        }
        catch (InvalidDataException)
        {
            return; // reliable roster for a new slot has not arrived yet
        }
        _interpolator.Add(snapshot);
        _localPlayer.Reconcile(snapshot, ack);
        SnapshotApplied?.Invoke(snapshot);
    }

    public void Handle(in PlayerModifiersMsg msg)
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
            _log.Error(e, "dropped malformed local player modifiers");
            return;
        }
        _hud.Configure(StatsPipeline.Resolve(_config, modifiers));
        _localPlayer.SetModifiers(modifiers);
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

        Vector2? localCameraPosition = null;
        if (_localPlayer.Initialized)
        {
            PlayerState local = _localPlayer.State;
            localCameraPosition = new Vector2(
                local.Position.X,
                local.Position.Y - SimConfig.PLAYER_HALF_HEIGHT) +
                _localPlayer.CorrectionOffset;
        }
        _spectator.Present(state.Players, localCameraPosition, NewestSnapshotTick);

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
