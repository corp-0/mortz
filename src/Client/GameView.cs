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
    [Export] private GameMap _gameMap = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;
    [Export] private MortarViewManager _mortars = null!;
    [Export] private Hud _hud = null!;

    /// <summary>Diagnostics tap: a snapshot was buffered and reconciled.</summary>
    public event Action<Snapshot>? SnapshotApplied;

    private readonly SnapshotInterpolator _interpolator = new();
    private MortarReplicaSet _remoteMortars = null!;
    private readonly Dictionary<byte, int> _peersBySlot = new();

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
    }

    public override void _ExitTree()
    {
        NetworkManager.Instance.SnapshotReceived -= OnSnapshotReceived;
        CarveMsg.Received -= OnCarve;
        ShellRetireMsg.Received -= OnShellRetire;
        MortarLifecycleMsg.Received -= OnMortarLifecycle;
        MortarCorrectionMsg.Received -= OnMortarCorrection;
        RosterMsg.Received -= OnRoster;
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

    public override void _PhysicsProcess(double delta)
    {
        if (_remoteMortars != null)
            _remoteMortars.Tick();
    }

    public override void _Process(double delta)
    {
        // Predicted destruction: our shells carve the instant they land.
        foreach ((int seq, Vec2 pos) in _localPlayer.DrainImpacts())
            _gameMap.PredictCarve(seq, new Vector2(pos.X, pos.Y));

        InterpolatedState? state = _interpolator.Advance((float)delta);
        if (state == null)
            return;

        int localId = Multiplayer.GetUniqueId();
        _ropes.Segments.Clear();
        _players.BeginFrame();

        foreach (RenderPlayer player in state.Players)
        {
            if (player.PeerId == localId)
                continue;
            _players.Place(player.PeerId, new PlayerViewState(
                new Vector2(player.Position.X, player.Position.Y), player.Aim, player.Skin,
                player.Ammo, player.ReloadTicks, player.Health, player.RespawnTicks,
                player.ParryTicks, player.DashCooldown));
            if (player.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(player.Position),
                    new Vector2(player.RopePoint.X, player.RopePoint.Y)));
        }

        if (_localPlayer.Initialized)
        {
            PlayerState local = _localPlayer.State;
            Vector2 feet = new Vector2(local.Position.X, local.Position.Y) + _localPlayer.CorrectionOffset;
            _players.Place(localId, new PlayerViewState(
                feet, _localPlayer.Aim, local.Skin, local.Ammo, local.ReloadTicks,
                local.Health, local.RespawnTicks, local.ParryTicks, local.DashCooldown));
            _hud.UpdateFrom(local);
            if (local.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(local.Position) + _localPlayer.CorrectionOffset,
                    new Vector2(local.RopePoint.X, local.RopePoint.Y)));
        }

        _players.Prune();
        _mortars.SyncRemote(_remoteMortars.Render());
        _mortars.SyncPredicted(_localPlayer.Shells);
    }

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
