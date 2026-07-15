using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// Composition root of the in-game screen. Routes incoming snapshots into the
/// interpolation clock and the local player's reconciliation, then pushes
/// each rendered frame out to the map, player, mortar and rope views. All the
/// pieces are separate nodes, wired in GameView.tscn.
/// </summary>
public partial class GameView : Node2D
{
    private const float IMPACT_HOLD_SECONDS = 0.12f;
    private const float REPLAY_ZOOM = 1.65f;

    [Export] private GameMap _gameMap = null!;
    [Export] private EffectsSpawner _effects = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;
    [Export] private MortarViewManager _mortars = null!;
    [Export] private Hud _hud = null!;
    [Export] private Camera2D _replayCamera = null!;

    /// <summary>Diagnostics tap: a snapshot was buffered and reconciled.</summary>
    public event Action<Snapshot>? SnapshotApplied;

    private readonly SnapshotInterpolator _interpolator = new();
    private MortarReplicaSet _remoteMortars = null!;
    private readonly Dictionary<byte, int> _peersBySlot = new();
    private readonly ReplayHistory _replayHistory = new();
    private FinalKillMsg? _pendingFinalKill;
    private FinalKillMsg _finalKill;
    private ReplayClip? _replayClip;
    private float _replayCursor;
    private float _impactHold;
    private bool _impactPlayed;
    private bool _replaying;
    private bool _matchFrozen;
    private Vector2 _replayCameraStart;
    private Vector2 _replayCameraStartZoom;
    private Vector2 _replayCameraStartOffset;
    private float _replayCameraStartRotation;

    public int NewestSnapshotTick => _interpolator.NewestTick;
    public float RenderTick => _interpolator.RenderTick;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(MapPackage map, MatchConfig config,
        TerrainSyncEncoding terrainEncoding, byte[] terrainData)
    {
        _gameMap.Initialize(map, config, terrainEncoding, terrainData);
        _localPlayer.Initialize(new Predictor(_gameMap.Mask, config));
        _remoteMortars = new MortarReplicaSet(_gameMap.Mask, config);
        // Remote players render with the base stats until perks exist.
        PlayerStats stats = PlayerStats.Resolve(config);
        _players.Configure(stats);
        _hud.Configure(stats);
    }

    public override void _Ready()
    {
        NetworkManager.Instance.SnapshotReceived += OnSnapshotReceived;
        CarveMsg.Received += OnCarve;
        ShellRetireMsg.Received += OnShellRetire;
        MortarLifecycleMsg.Received += OnMortarLifecycle;
        MortarCorrectionMsg.Received += OnMortarCorrection;
        RosterMsg.Received += OnRoster;
        FinalKillMsg.Received += OnFinalKill;
    }

    public override void _ExitTree()
    {
        ClientClock.Reset();
        _replayCamera.Enabled = false;
        NetworkManager.Instance.SnapshotReceived -= OnSnapshotReceived;
        CarveMsg.Received -= OnCarve;
        ShellRetireMsg.Received -= OnShellRetire;
        MortarLifecycleMsg.Received -= OnMortarLifecycle;
        MortarCorrectionMsg.Received -= OnMortarCorrection;
        RosterMsg.Received -= OnRoster;
        FinalKillMsg.Received -= OnFinalKill;
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

    private void OnRoster(RosterMsg msg)
    {
        _peersBySlot.Clear();
        int count = Math.Min(msg.PeerIds.Length, msg.Slots.Length);
        for (int i = 0; i < count; i++)
            if (msg.Slots[i] is > 0 and <= NetConfig.MAX_PLAYERS)
                _peersBySlot[msg.Slots[i]] = (int)msg.PeerIds[i];
    }

    /// <summary>Our shell exploded server-side; retire the predicted copy so it
    /// can't fly on and carve a ghost. Deflected shells carry -1 and are skipped.</summary>
    private void OnCarve(CarveMsg msg)
    {
        if (msg.SpawnSeq >= 0 && msg.OwnerId == Multiplayer.GetUniqueId() &&
            _localPlayer.RetireShell(msg.SpawnSeq))
            GD.Print($"[client] retired shell seq {msg.SpawnSeq} (authoritative explosion)");
    }

    /// <summary>Reliable retirement is the delivery guarantee when a parry takes
    /// over one of our shells. Settle the carve even if its impact was queued but
    /// had not reached GameMap yet; the lifecycle deflect path below is the
    /// low-latency fallback and both paths are idempotent.</summary>
    private void OnShellRetire(ShellRetireMsg msg)
    {
        bool hadPrediction = _localPlayer.RetireShell(msg.SpawnSeq);
        bool hadCarve = _gameMap.RevertPredictedCarve(msg.SpawnSeq);
        if (hadPrediction || hadCarve)
            GD.Print($"[client] retired shell seq {msg.SpawnSeq} (reliable server event)");
    }

    private void OnMortarLifecycle(MortarLifecycleMsg msg)
    {
        if (!MortarWire.TryReadLifecycle(msg.Events, out int tick,
            out List<SimWorld.MortarEvent> events))
        {
            GD.PrintErr("[client] dropped malformed mortar lifecycle batch");
            return;
        }
        foreach (SimWorld.MortarEvent e in events)
        {
            switch (e.Kind)
            {
                case SimWorld.MortarEventKind.Spawn:
                    _remoteMortars.Spawn(e.State, tick, NewestSnapshotTick);
                    if (e.State.FiredBy != Multiplayer.GetUniqueId())
                        Sfx.PlayAt(Sfx.Sounds.MortarFire,
                            new Vector2(e.State.Position.X, e.State.Position.Y));
                    break;
                case SimWorld.MortarEventKind.Deflect:
                    _remoteMortars.Deflect(e.State, tick, NewestSnapshotTick);
                    RetireDeflectedPrediction(e.State);
                    Sfx.PlayAt(Sfx.Sounds.ParrySuccess,
                        new Vector2(e.State.Position.X, e.State.Position.Y));
                    break;
                case SimWorld.MortarEventKind.End:
                    RetireEndedMortar(e.State.Id);
                    break;
            }
        }
    }

    private void RetireDeflectedPrediction(in MortarState state)
    {
        if (state.FiredBy != Multiplayer.GetUniqueId())
            return;
        bool hadShell = _localPlayer.RetireShell(state.SpawnSeq);
        bool hadCarve = _gameMap.RevertPredictedCarve(state.SpawnSeq);
        if (hadShell || hadCarve)
            GD.Print($"[client] retired shell seq {state.SpawnSeq} (deflected)");
    }

    private void RetireEndedMortar(ushort id)
    {
        if (_remoteMortars.TryEnd(id, out MortarState state) &&
            state.FiredBy == Multiplayer.GetUniqueId())
            _localPlayer.RetireShell(state.SpawnSeq);
    }

    private void OnMortarCorrection(MortarCorrectionMsg msg)
    {
        if (!_remoteMortars.Correct(msg.States, msg.Tick, NewestSnapshotTick))
            GD.PrintErr("[client] dropped malformed mortar correction");
    }

    private void OnFinalKill(FinalKillMsg msg)
    {
        if (_matchFrozen)
            return;
        _matchFrozen = true;
        _localPlayer.Frozen = true;
        _pendingFinalKill = msg;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_matchFrozen && _remoteMortars != null)
            _remoteMortars.Tick();
    }

    public override void _Process(double delta)
    {
        if (_pendingFinalKill is { } finalKill)
        {
            BeginReplay(finalKill);
            _pendingFinalKill = null;
        }
        if (_replaying)
        {
            AdvanceReplay((float)delta);
            return;
        }
        if (_matchFrozen)
            return;

        // Predicted destruction: our shells carve the instant they land.
        foreach ((int seq, Vec2 pos) in _localPlayer.DrainImpacts())
            _gameMap.PredictCarve(seq, new Vector2(pos.X, pos.Y));

        InterpolatedState? state = _interpolator.Advance((float)delta);
        if (state == null)
            return;

        int localId = Multiplayer.GetUniqueId();
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
                player.ParryTicks, player.DashCooldown);
            _players.Place(player.PeerId, viewState);
            replayPlayers.Add(new ReplayPlayer(player.PeerId, viewState));
            if (player.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(player.Position),
                    new Vector2(player.RopePoint.X, player.RopePoint.Y)));
        }

        if (_localPlayer.Initialized)
        {
            PlayerState local = _localPlayer.State;
            Vector2 feet = new Vector2(local.Position.X, local.Position.Y) + _localPlayer.CorrectionOffset;
            PlayerViewState viewState = new(
                feet, _localPlayer.Aim, local.Skin, local.Ammo, local.ReloadTicks,
                local.Health, local.RespawnTicks, local.ParryTicks, local.DashCooldown);
            _players.Place(localId, viewState);
            replayPlayers.Add(new ReplayPlayer(localId, viewState));
            _hud.UpdateFrom(local);
            if (local.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(local.Position) + _localPlayer.CorrectionOffset,
                    new Vector2(local.RopePoint.X, local.RopePoint.Y)));
        }

        _players.Prune();
        IReadOnlyList<RenderMortar> remoteMortars = _remoteMortars.Render();
        IReadOnlyList<(int SpawnSeq, MortarState Shell)> predictedMortars = _localPlayer.Shells;
        _mortars.SyncRemote(remoteMortars);
        _mortars.SyncPredicted(predictedMortars);

        ReplayMortar[] replayMortars =
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
        _replayHistory.Add(new ReplayFrame(
            RenderTick,
            replayPlayers.ToArray(),
            replayMortars,
            _ropes.Segments.ToArray()));
    }

    private void BeginReplay(FinalKillMsg finalKill)
    {
        ReplayClip? clip = _replayHistory.Capture(finalKill.Tick);
        if (clip == null)
        {
            _effects.PlayWithoutReplay(finalKill);
            return;
        }

        _finalKill = finalKill;
        _replayClip = clip;
        _replayCursor = clip.StartTick;
        _impactHold = 0;
        _impactPlayed = false;
        _replaying = true;
        ClientClock.BeginReplay();
        _effects.BeginReplay();
        _mortars.BeginReplay();
        _gameMap.BeginReplayTerrain(finalKill);

        ReplayFrame first = clip.Sample(clip.StartTick);
        CloneGameplayCamera(first, finalKill);
        _replayCamera.Enabled = true;
        _players.SetReplayActive(true);
        RenderReplayFrame(first, hideVictim: false);
    }

    private void AdvanceReplay(float delta)
    {
        ReplayClip clip = _replayClip!;
        if (_impactHold > 0)
        {
            _impactHold -= delta;
            if (_impactHold > 0)
                return;
            EndReplay();
            return;
        }

        _replayCursor = Math.Min(
            clip.EndTick,
            _replayCursor + delta * SimConfig.TICK_RATE * ClientClock.TimeScale);
        float progress = (clip.EndTick - clip.StartTick) > 0
            ? (_replayCursor - clip.StartTick) / (clip.EndTick - clip.StartTick)
            : 1f;
        bool atImpact = _replayCursor >= clip.EndTick;
        RenderReplayFrame(clip.Sample(_replayCursor), hideVictim: atImpact);
        UpdateReplayCamera(progress);
        if (!atImpact || _impactPlayed)
            return;
        _impactPlayed = true;
        _gameMap.ShowReplayImpact();
        _effects.PlayReplayImpact(_finalKill);
        _impactHold = IMPACT_HOLD_SECONDS;
    }

    private void RenderReplayFrame(ReplayFrame frame, bool hideVictim)
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

    private void UpdateReplayCamera(float progress)
    {
        float eased = progress * progress * (3f - 2f * progress);
        Vector2 focus = new(_finalKill.ImpactX, _finalKill.ImpactY);
        _replayCamera.GlobalPosition = _replayCameraStart.Lerp(focus, eased);
        _replayCamera.Zoom = _replayCameraStartZoom.Lerp(
            _replayCameraStartZoom * REPLAY_ZOOM, eased);
        _replayCamera.Offset = _replayCameraStartOffset.Lerp(Vector2.Zero, eased);
        _replayCamera.GlobalRotation = Mathf.LerpAngle(_replayCameraStartRotation, 0, eased);
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
            int localId = Multiplayer.GetUniqueId();
            int localIndex = Array.FindIndex(first.Players, player => player.PeerId == localId);
            _replayCamera.GlobalPosition = localIndex >= 0
                ? first.Players[localIndex].State.Feet -
                  new Vector2(0, SimConfig.PLAYER_HALF_HEIGHT)
                : new Vector2(finalKill.ImpactX, finalKill.ImpactY);
            _replayCamera.GlobalRotation = 0;
            _replayCamera.Zoom = Vector2.One;
            _replayCamera.Offset = Vector2.Zero;
        }

        _replayCameraStart = _replayCamera.GlobalPosition;
        _replayCameraStartZoom = _replayCamera.Zoom;
        _replayCameraStartOffset = _replayCamera.Offset;
        _replayCameraStartRotation = _replayCamera.GlobalRotation;
    }

    private void EndReplay()
    {
        _replaying = false;
        _mortars.EndReplay();
        _effects.EndReplay();
        _gameMap.EndReplayTerrain();
        _replayClip = null;
    }

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
