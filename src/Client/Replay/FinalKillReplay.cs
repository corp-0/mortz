using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Effects;
using Mortz.Client.Match;
using Mortz.Client.Spectating;
using Mortz.Client.Views;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Net;

namespace Mortz.Client.Replay;

/// <summary>Owns the final-kill cinematic: event handling, render history,
/// freeze state, playback, camera, temporary terrain, effects, and hold.</summary>
[Meta(typeof(IAutoNode))]
public partial class FinalKillReplay : Node, IHandle<FinalKillMsg>
{
    private const float IMPACT_HOLD_SECONDS = 0.12f;
    private const float REPLAY_ZOOM = 1.65f;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private GameMap Map => this.DependOn<GameMap>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    [Export] private EffectsSpawner _effects = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;
    [Export] private MortarViewManager _mortars = null!;
    [Export] private Camera2D _replayCamera = null!;
    [Export] private SpectatorController _spectator = null!;

    private readonly ReplayHistory _history = new();
    private FinalKillMsg? _pendingFinalKill;
    private FinalKillMsg _finalKill;
    private ReplayClip? _clip;
    private float _cursor;
    private float _impactHold;
    private bool _impactPlayed;
    private bool _replaying;
    private bool _matchFrozen;
    private Vector2 _cameraStart;
    private Vector2 _cameraStartZoom;
    private Vector2 _cameraStartOffset;
    private float _cameraStartRotation;

    public bool MatchFrozen => _matchFrozen;

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _routed = Router;
        _routed.Add(this);
    }

    public override void _ExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
        ClientClock.Reset();
        _replayCamera.Enabled = false;
    }

    public void Record(
        float tick,
        IReadOnlyList<ReplayPlayer> players,
        IReadOnlyList<RenderMortar> remoteMortars,
        IReadOnlyList<(int SpawnSeq, MortarState Shell)> predictedMortars,
        IReadOnlyList<(Vector2 From, Vector2 To)> ropes,
        int localId)
    {
        ReplayMortar[] mortars =
        [
            .. remoteMortars
                .Where(mortar => mortar.OwnerId != localId || mortar.Deflected)
                .Select(mortar => new ReplayMortar(
                    mortar.Id,
                    new Vector2(mortar.Position.X, mortar.Position.Y),
                    mortar.Velocity)),
            .. predictedMortars.Select(entry => new ReplayMortar(
                (1L << 32) | (uint)entry.SpawnSeq,
                new Vector2(entry.Shell.Position.X, entry.Shell.Position.Y),
                entry.Shell.Velocity)),
        ];
        _history.Add(new ReplayFrame(tick, players.ToArray(), mortars, ropes.ToArray()));
    }

    /// <summary>Advances replay work. True means live rendering stays frozen.</summary>
    public bool ConsumeFrame(float delta)
    {
        if (_pendingFinalKill is FinalKillMsg finalKill)
        {
            Begin(finalKill);
            _pendingFinalKill = null;
        }
        if (_replaying)
            Advance(delta);
        return _matchFrozen;
    }

    public void Handle(in FinalKillMsg msg)
    {
        if (_matchFrozen)
            return;
        _matchFrozen = true;
        _localPlayer.Frozen = true;
        _spectator.SetReplayActive(true);
        _pendingFinalKill = msg;
    }

    private void Begin(FinalKillMsg finalKill)
    {
        ReplayClip? clip = _history.Capture(finalKill.Tick);
        if (clip == null)
        {
            _effects.PlayWithoutReplay(finalKill);
            return;
        }

        _finalKill = finalKill;
        _clip = clip;
        _cursor = clip.StartTick;
        _impactHold = 0;
        _impactPlayed = false;
        _replaying = true;
        ClientClock.BeginReplay();
        _effects.BeginReplay();
        _mortars.BeginReplay();
        Map.BeginReplayTerrain(finalKill);

        ReplayFrame first = clip.Sample(clip.StartTick);
        CloneGameplayCamera(first, finalKill);
        _replayCamera.Enabled = true;
        _players.SetReplayActive(true);
        Render(first, hideVictim: false);
    }

    private void Advance(float delta)
    {
        ReplayClip clip = _clip!;
        if (_impactHold > 0)
        {
            _impactHold -= delta;
            if (_impactHold <= 0)
                FinishPlayback();
            return;
        }

        _cursor = Math.Min(
            clip.EndTick,
            _cursor + delta * SimConfig.TICK_RATE * ClientClock.TimeScale);
        float progress = (clip.EndTick - clip.StartTick) > 0
            ? (_cursor - clip.StartTick) / (clip.EndTick - clip.StartTick)
            : 1f;
        bool atImpact = _cursor >= clip.EndTick;
        Render(clip.Sample(_cursor), hideVictim: atImpact);
        UpdateCamera(progress);
        if (!atImpact || _impactPlayed)
            return;
        _impactPlayed = true;
        Map.ShowReplayImpact();
        _effects.PlayReplayImpact(_finalKill);
        _impactHold = IMPACT_HOLD_SECONDS;
    }

    private void Render(ReplayFrame frame, bool hideVictim)
    {
        _players.BeginFrame();
        foreach (ReplayPlayer player in frame.Players)
        {
            PlayerViewState state = player.State;
            if (hideVictim && player.PeerId == _finalKill.VictimId)
                state = state with { RespawnTicks = 1, Health = 0 };
            _players.Place(player.PeerId, state);
        }
        _players.Prune();
        _mortars.SyncReplay(frame.Mortars);
        _ropes.Segments.Clear();
        _ropes.Segments.AddRange(frame.Ropes);
    }

    private void UpdateCamera(float progress)
    {
        float eased = progress * progress * (3f - 2f * progress);
        Vector2 focus = new(_finalKill.ImpactX, _finalKill.ImpactY);
        _replayCamera.GlobalPosition = _cameraStart.Lerp(focus, eased);
        _replayCamera.Zoom = _cameraStartZoom.Lerp(_cameraStartZoom * REPLAY_ZOOM, eased);
        _replayCamera.Offset = _cameraStartOffset.Lerp(Vector2.Zero, eased);
        _replayCamera.GlobalRotation = Mathf.LerpAngle(_cameraStartRotation, 0, eased);
    }

    private void CloneGameplayCamera(ReplayFrame first, FinalKillMsg finalKill)
    {
        Camera2D? gameplayCamera = GetViewport().GetCamera2D();
        if (gameplayCamera != null && gameplayCamera != _replayCamera)
        {
            _replayCamera.GlobalTransform = gameplayCamera.GlobalTransform;
            _replayCamera.Zoom = gameplayCamera.Zoom;
            _replayCamera.Offset = gameplayCamera.Offset;
            _replayCamera.IgnoreRotation = gameplayCamera.IgnoreRotation;
            _replayCamera.AnchorMode = gameplayCamera.AnchorMode;
        }
        else
        {
            int localId = Network.LocalPeerId;
            int localIndex = Array.FindIndex(first.Players, player => player.PeerId == localId);
            _replayCamera.GlobalPosition = localIndex >= 0
                ? first.Players[localIndex].State.Feet -
                  new Vector2(0, SimConfig.PLAYER_HALF_HEIGHT)
                : new Vector2(finalKill.ImpactX, finalKill.ImpactY);
            _replayCamera.GlobalRotation = 0;
            _replayCamera.Zoom = Vector2.One;
            _replayCamera.Offset = Vector2.Zero;
        }

        _cameraStart = _replayCamera.GlobalPosition;
        _cameraStartZoom = _replayCamera.Zoom;
        _cameraStartOffset = _replayCamera.Offset;
        _cameraStartRotation = _replayCamera.GlobalRotation;
    }

    private void FinishPlayback()
    {
        _replaying = false;
        _mortars.EndReplay();
        _effects.EndReplay();
        Map.EndReplayTerrain();
        _clip = null;
    }
}
