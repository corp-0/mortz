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

    public int NewestSnapshotTick => _interpolator.NewestTick;
    public float RenderTick => _interpolator.RenderTick;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(MapPackage map, MatchConfig config, byte[] removedData)
    {
        _gameMap.Initialize(map, config, removedData);
        _localPlayer.Initialize(new Predictor(_gameMap.Mask, config));
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
    }

    public override void _ExitTree()
    {
        NetworkManager.Instance.SnapshotReceived -= OnSnapshotReceived;
        CarveMsg.Received -= OnCarve;
        ShellRetireMsg.Received -= OnShellRetire;
    }

    private void OnSnapshotReceived(byte[] data, int ack)
    {
        Snapshot snapshot = Snapshot.Deserialize(data);
        _interpolator.Add(snapshot);
        _localPlayer.Reconcile(snapshot, ack);
        RetireDeflectedShells(snapshot);
        SnapshotApplied?.Invoke(snapshot);
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
    /// had not reached GameMap yet; the snapshot path below is the low-latency
    /// fallback because cross-channel delivery order is not guaranteed.</summary>
    private void OnShellRetire(ShellRetireMsg msg)
    {
        bool hadPrediction = _localPlayer.RetireShell(msg.SpawnSeq);
        bool hadCarve = _gameMap.RevertPredictedCarve(msg.SpawnSeq);
        if (hadPrediction || hadCarve)
            GD.Print($"[client] retired shell seq {msg.SpawnSeq} (reliable server event)");
    }

    /// <summary>A parry took one of our shells over (a deflected shell in the
    /// snapshot). This may arrive before the reliable retirement message, so
    /// retire immediately; both paths are idempotent. Revert its carve if it
    /// already laid one because the deflected carve is -1 and cannot confirm it.</summary>
    private void RetireDeflectedShells(Snapshot snapshot)
    {
        foreach (MortarState m in snapshot.Mortars)
        {
            if (!m.Deflected || m.FiredBy != Multiplayer.GetUniqueId())
                continue;
            bool hadShell = _localPlayer.RetireShell(m.SpawnSeq);
            bool hadCarve = _gameMap.RevertPredictedCarve(m.SpawnSeq);
            if (hadShell || hadCarve)
            {
                GD.Print($"[client] retired shell seq {m.SpawnSeq} (deflected)");
            }
        }
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
            _players.Place(player.PeerId, new Vector2(player.Position.X, player.Position.Y),
                player.Aim, player.Skin, player.Ammo, player.ReloadTicks, player.Health, player.RespawnTicks,
                player.ParryTicks, player.DashCooldown);
            if (player.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(player.Position),
                    new Vector2(player.RopePoint.X, player.RopePoint.Y)));
        }

        if (_localPlayer.Initialized)
        {
            PlayerState local = _localPlayer.State;
            Vector2 feet = new Vector2(local.Position.X, local.Position.Y) + _localPlayer.CorrectionOffset;
            _players.Place(localId, feet, _localPlayer.Aim, local.Skin, local.Ammo, local.ReloadTicks,
                local.Health, local.RespawnTicks, local.ParryTicks, local.DashCooldown);
            _hud.UpdateFrom(local);
            if (local.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(local.Position) + _localPlayer.CorrectionOffset,
                    new Vector2(local.RopePoint.X, local.RopePoint.Y)));
        }

        _players.Prune();
        _mortars.SyncRemote(state.Mortars);
        _mortars.SyncPredicted(_localPlayer.Shells);
    }

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
