using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Announcements;
using Mortz.Client.Match.PlayerHud;
using Mortz.Client.Players;
using Mortz.Client.Replay;
using Mortz.Client.Replication;
using Mortz.Client.Spectating;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared;
#if TOOLS
using Mortz.Client.E2E;
#endif

namespace Mortz.Client.Match;

/// <summary>Composition root of the in-game screen, wired in GameView.tscn.</summary>
[Meta(typeof(IAutoNode))]
public partial class GameView : Node2D,
    IProvide<ClientMatchRuntime>,
    IProvide<ClientMatchState>,
    IProvide<IAnnouncementDirector>,
    IProvide<GameMap>
{
    [Export] private PackedScene _gameMapScene = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;
    [Export] private MortarClient _mortarClient = null!;
    [Export] private PlayerStatusHud _hud = null!;
    [Export] private FinalKillReplay _finalKillReplay = null!;
    [Export] private SpectatorController _spectator = null!;

    [Dependency] private INetwork Network => this.DependOn<INetwork>();

    [Dependency] private ClientPlayers Players => this.DependOn<ClientPlayers>();

    IAnnouncementDirector IProvide<IAnnouncementDirector>.Value() => _runtime.Announcements;
    ClientMatchState IProvide<ClientMatchState>.Value() => _matchState;
    GameMap IProvide<GameMap>.Value() => _gameMap;

    public override void _Notification(int what) => this.Notify(what);

    /// <summary>Diagnostics tap: a snapshot was buffered and reconciled.</summary>
    public event Action<Snapshot>? SnapshotApplied;

    private ClientMatchRuntime _runtime = null!;
    private SnapshotInterpolator _interpolator => _runtime.Interpolator;
    ClientMatchRuntime IProvide<ClientMatchRuntime>.Value() => _runtime;
    private GameMap _gameMap = null!;
    private ClientMatchState _matchState = null!;
    private LiveMatchFrameBuilder _frameBuilder = null!;
    private MatchSceneRenderer _renderer = null!;

    /// <summary>The map this match is played on; set by Initialize.</summary>
    public string MapId { get; private set; } = "";

    public int NewestSnapshotTick => _interpolator.NewestTick;
    public float RenderTick => _interpolator.RenderTick;

    /// <summary>Must be called right after instantiating, before entering the tree:
    /// it mounts the map the other nodes depend on.</summary>
    public void Initialize(ClientMatchRuntime runtime, MapPackage map)
    {
        _runtime = runtime;
        ClientMatchState matchState = runtime.State;
        _matchState = matchState;
        MapId = map.MapId;
        _gameMap = _gameMapScene.Instantiate<GameMap>();
        _gameMap.Initialize(map, runtime.Terrain);
        _gameMap.SetZonesVisible(PlayerView.DrawSimBoxes);
        AddChild(_gameMap);
        // Terrain has to draw under the players, mortars and ropes; AddChild
        // would leave it on top.
        MoveChild(_gameMap, 0);
        _localPlayer.Initialize(runtime);
        _spectator.Initialize(new Vector2(map.Width / 2f, map.Height / 2f));
        _frameBuilder = new LiveMatchFrameBuilder((peerId, snapshotSkin) =>
            Players.Find(peerId)?.Skin ?? snapshotSkin);
        _renderer = new MatchSceneRenderer(_players, _mortarClient.ViewManager, _ropes);
        _finalKillReplay.Initialize(_renderer);
        _hud.Configure(PlayerStats.Resolve(runtime.Config.ToMutable()));
        _hud.Visible = matchState.Participation.Activity == MatchActivity.ACTIVE;
    }

    public void OnResolved()
    {
        _runtime.SnapshotApplied += OnSnapshotApplied;
        Players.MatchStatsChanged += OnMatchStatsChanged;
        _matchState.ParticipationChanged += OnParticipationChanged;
        this.Provide();
        ClientPlayer? local = Players.Find(Network.LocalPeerId);
        if (local != null)
            OnMatchStatsChanged(local);
#if TOOLS
        // Built in code, never declared in GameView.tscn: see ClientE2ERoot.
        ClientE2ERoot.AttachMatch(this, _localPlayer);
#endif
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
        if (_runtime == null)
            return;
        _runtime.SnapshotApplied -= OnSnapshotApplied;
        Players.MatchStatsChanged -= OnMatchStatsChanged;
        _matchState.ParticipationChanged -= OnParticipationChanged;
    }

    private void OnParticipationChanged(MatchParticipation participation)
    {
        _hud.Visible = participation.Activity == MatchActivity.ACTIVE;
    }

    private void OnSnapshotApplied(Snapshot snapshot) => SnapshotApplied?.Invoke(snapshot);

    private void OnMatchStatsChanged(ClientPlayer player)
    {
        if (player.PeerId != Network.LocalPeerId || player.Match == null)
            return;
        _hud.Configure(player.Match.Stats);
    }

    public override void _Process(double delta)
    {
        if (_finalKillReplay.ConsumeFrame((float)delta))
            return;

        InterpolatedState? state = _interpolator.Advance((float)delta);
        if (state == null)
            return;

        int localId = Network.LocalPeerId;
        PlayerState? localState = _localPlayer.Initialized ? _localPlayer.State : null;
        PresentedMatchFrame frame = _frameBuilder.Build(
            RenderTick,
            state,
            localId,
            localState,
            _localPlayer.CorrectionOffset,
            _localPlayer.Aim,
            _mortarClient.SampleRemoteMortars(),
            _localPlayer.Shells,
            _localPlayer.CompletedShells);
        _renderer.Apply(frame, MatchRenderMode.LIVE);
        _finalKillReplay.Record(frame);

        Vector2? localCameraPosition = null;
        if (localState is PlayerState local)
        {
            _hud.UpdateFrom(local);
            localCameraPosition = new Vector2(
                                      local.Position.X,
                                      local.Position.Y - SimConfig.PLAYER_HALF_HEIGHT) +
                                  _localPlayer.CorrectionOffset;
        }

        _spectator.Present(state.Players, localCameraPosition, NewestSnapshotTick);
    }
}
