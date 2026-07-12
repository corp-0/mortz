using Godot;
using Mortz.Core;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// Renders the networked world and sends local input. The local player is
/// client-side predicted (instant response, corrections eased in over a few
/// frames); remote players render from interpolated snapshots. The map itself
/// (layers, collision mask, carves) lives in GameMap.
/// </summary>
public partial class GameView : Node2D
{
    /// <summary>How fast reconciliation corrections blend away (per second).</summary>
    private const float CORRECTION_DECAY = 10f;

    private static readonly bool _netStats = CmdArgs.HasFlag("--net-stats");

    // Perceived-lag probe, run as a pair of clients on one machine (shared
    // wall clock): --probe-input injects walking bursts and logs the press
    // edge, --probe-watch logs when a remote player's rendered position first
    // moves after being still. Timestamp difference = true press-to-screen lag.
    private static readonly bool _probeInput = CmdArgs.HasFlag("--probe-input");
    private static readonly bool _probeWatch = CmdArgs.HasFlag("--probe-watch");

    [Export] private GameMap _gameMap = null!;
    [Export] private RopeOverlay _ropes = null!;
    [Export] private PackedScene _playerScene = null!;

    private static readonly bool _testFire = CmdArgs.HasFlag("--test-fire");

    private readonly SnapshotBuffer _snapshots = new();
    private readonly Dictionary<int, PlayerView> _players = new();
    private readonly Dictionary<ushort, MortarView> _mortars = new();
    // Own shells, rendered from prediction (keyed by the input seq that fired).
    private readonly Dictionary<int, MortarView> _ownMortars = new();
    private Predictor _predictor = null!;
    private Vector2 _correctionOffset;
    private byte _localAim;
    private float _renderTick = -1;
    private int _lastLoggedSecond = -1;
    private bool _testCarveSent;

    // --probe-watch state: last stable position and whether we're waiting for it to move.
    private Vector2 _probeRef;
    private ulong _probeStableSince;
    private bool _probeArmed;

    // --net-stats accumulators, reported once per second.
    private readonly Dictionary<int, ulong> _inputSendTimes = new();
    private int _statFrames;
    private int _lastAckSeen = -1;
    private ulong _lastSnapshotMsec;
    private double _rttSum, _rttMax;
    private int _rttCount;
    private float _corrSum;
    private int _corrCount;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(MapPackage map, byte[] removedData)
    {
        _gameMap.Initialize(map, removedData);
        _predictor = new Predictor(_gameMap.Mask);
    }

    public override void _Ready()
    {
        NetworkManager.Instance.SnapshotReceived += OnSnapshotReceived;
    }

    public override void _ExitTree()
    {
        NetworkManager.Instance.SnapshotReceived -= OnSnapshotReceived;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { PhysicalKeycode: Key.F3, Pressed: true, Echo: false })
            PlayerView.DrawSimBoxes = !PlayerView.DrawSimBoxes;
    }

    // ---- state flow ----

    private void OnSnapshotReceived(byte[] data, int ack)
    {
        Snapshot snapshot = Snapshot.Deserialize(data);
        _snapshots.Add(snapshot);

        int localId = Multiplayer.GetUniqueId();
        foreach (PlayerState player in snapshot.Players)
        {
            if (player.PeerId != localId)
                continue;
            if (!_predictor.Initialized)
                GD.Print("[client] prediction initialized");
            Vec2 correction = _predictor.Reconcile(player, ack);
            // Small disagreements ease in; teleport-scale ones (respawn after a
            // death pit) snap immediately instead of sliding across the map.
            if (correction.Length() > 150)
                _correctionOffset = Vector2.Zero;
            else
                _correctionOffset += new Vector2(correction.X, correction.Y);
            if (_netStats)
                RecordAck(ack, correction);
            SendTestCarveOnce();
            break;
        }

        // Heartbeat log for headless E2E verification, once per sim-second.
        int second = snapshot.Tick / SimConfig.TICK_RATE;
        if (second != _lastLoggedSecond)
        {
            _lastLoggedSecond = second;
            GD.Print($"[client] snapshot tick {snapshot.Tick}, {snapshot.Players.Length} player(s)");
        }
    }

    /// <summary>Headless E2E hook: carve the first destructible spot in the map once.</summary>
    private void SendTestCarveOnce()
    {
        if (_testCarveSent || !CmdArgs.HasFlag("--test-carve"))
            return;
        _testCarveSent = true;
        for (int y = 0; y < _gameMap.Mask.Height; y++)
            for (int x = 0; x < _gameMap.Mask.Width; x++)
            {
                if (_gameMap.Mask.Get(x, y) == TerrainMaterial.Destructible)
                {
                    GD.Print($"[client] requesting test carve at ({x},{y})");
                    NetworkManager.Instance.RequestDebugCarve(x, y);
                    return;
                }
            }
    }

    public override void _PhysicsProcess(double delta)
    {
        InputButtons buttons = InputButtons.None;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left))
            buttons |= InputButtons.Left;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right))
            buttons |= InputButtons.Right;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))
            buttons |= InputButtons.Up;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))
            buttons |= InputButtons.Down;
        if (Input.IsPhysicalKeyPressed(Key.Space))
            buttons |= InputButtons.Jump;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            buttons |= InputButtons.Dash;
        if (Input.IsMouseButtonPressed(MouseButton.Right))
            buttons |= InputButtons.Rope;
        if (Input.IsMouseButtonPressed(MouseButton.Left))
            buttons |= InputButtons.Fire;
        if (Input.IsPhysicalKeyPressed(Key.R))
            buttons |= InputButtons.Reload;

        if (_predictor.Initialized)
        {
            Vector2 toMouse = GetGlobalMousePosition() - BodyCenter(_predictor.State.Position);
            if (toMouse.LengthSquared() > 1)
                _localAim = PlayerInput.AimFromVector(new Vec2(toMouse.X, toMouse.Y));
        }

        // Headless E2E: point-blank floor shots exercise explosion and
        // self-death. Set after the mouse block so the aim stays put.
        if (_testFire)
        {
            buttons |= InputButtons.Fire;
            _localAim = 64;
        }
        if (_probeInput)
            buttons = ProbeButtons();

        _predictor.LocalTick(new PlayerInput(buttons, _localAim));
        if (_predictor.NextSeq % NetConfig.TICKS_PER_INPUT_PACKET == 0)
        {
            NetworkManager.Instance.SendInputs(
                InputPacket.Encode(_predictor.RecentInputs(NetConfig.INPUT_REDUNDANCY)));
            if (_netStats)
                _inputSendTimes[_predictor.NextSeq - 1] = Time.GetTicksMsec();
        }

        if (_netStats && ++_statFrames >= SimConfig.TICK_RATE)
            PrintNetStats();
    }

    // ---- perceived-lag probe ----

    /// <summary>Half a second of walking every five seconds, alternating
    /// direction so the player ends up roughly where it started.</summary>
    private InputButtons ProbeButtons()
    {
        const int CYCLE = 5 * SimConfig.TICK_RATE;
        int phase = _predictor.NextSeq % CYCLE;
        if (phase >= SimConfig.TICK_RATE / 2)
            return InputButtons.None;
        if (phase == 0)
            GD.Print($"[probe] press unix={Time.GetUnixTimeFromSystem():F3}");
        return (_predictor.NextSeq / CYCLE) % 2 == 0 ? InputButtons.Right : InputButtons.Left;
    }

    /// <summary>Logs when a remote player's rendered position first moves
    /// after a second of standing still. Runs on the interpolated output, so
    /// it sees exactly what the screen shows.</summary>
    private void ProbeWatch(Vector2 pos)
    {
        ulong now = Time.GetTicksMsec();
        if (_probeArmed)
        {
            if ((pos - _probeRef).Length() > 0.3f)
            {
                GD.Print($"[probe] move unix={Time.GetUnixTimeFromSystem():F3}");
                _probeArmed = false;
                _probeRef = pos;
                _probeStableSince = now;
            }
            return;
        }
        if ((pos - _probeRef).Length() > 0.01f)
        {
            _probeRef = pos;
            _probeStableSince = now;
        }
        else if (now - _probeStableSince > 1000)
            _probeArmed = true;
    }

    // ---- --net-stats ----

    private void RecordAck(int ack, Vec2 correction)
    {
        _lastSnapshotMsec = Time.GetTicksMsec();
        _corrSum += correction.Length();
        _corrCount++;
        if (ack <= _lastAckSeen)
            return;
        _lastAckSeen = ack;
        if (_inputSendTimes.TryGetValue(ack, out ulong sentAt))
        {
            // Input send -> snapshot acking it. Remote-view latency is roughly
            // this plus the interpolation delay on the other client.
            double rtt = Time.GetTicksMsec() - sentAt;
            _rttSum += rtt;
            _rttCount++;
            _rttMax = Math.Max(_rttMax, rtt);
        }
        List<int> stale = new List<int>();
        foreach (int seq in _inputSendTimes.Keys)
            if (seq <= ack)
                stale.Add(seq);
        foreach (int seq in stale)
            _inputSendTimes.Remove(seq);
    }

    private void PrintNetStats()
    {
        _statFrames = 0;
        (double sent, double recv, double sentPk, double recvPk) = NetworkManager.Instance.PopWireStats();
        float interpTicks = _snapshots.NewestTick >= 0 ? _snapshots.NewestTick - _renderTick : -1;
        double snapAge = _lastSnapshotMsec > 0 ? Time.GetTicksMsec() - _lastSnapshotMsec : -1;
        double rttAvg = _rttCount > 0 ? _rttSum / _rttCount : -1;
        float corrAvg = _corrCount > 0 ? _corrSum / _corrCount : 0;
        GD.Print($"[stats] unix={Time.GetUnixTimeFromSystem():F3} seq={_predictor.NextSeq} " +
                 $"newest={_snapshots.NewestTick} renderTick={_renderTick:F1} interp={interpTicks:F1}tk " +
                 $"snapAge={snapAge:F0}ms rtt={rttAvg:F0}avg/{_rttMax:F0}max ms corr={corrAvg:F2}px " +
                 $"up={sent:F0}B/{sentPk:F0}pk down={recv:F0}B/{recvPk:F0}pk");
        _rttSum = 0; _rttMax = 0; _rttCount = 0;
        _corrSum = 0; _corrCount = 0;
    }

    public override void _Process(double delta)
    {
        if (_predictor is { Initialized: true })
        {
            // Predicted destruction: our shells carve the instant they land.
            foreach ((int seq, Vec2 pos) in _predictor.DrainImpacts())
                _gameMap.PredictCarve(seq, new Vector2(pos.X, pos.Y));
        }

        if (_snapshots.NewestTick < 0)
            return;

        // Advance the render clock at sim speed and keep it anchored
        // InterpolationDelayTicks behind the newest snapshot, correcting drift.
        float target = _snapshots.NewestTick - NetConfig.INTERPOLATION_DELAY_TICKS;
        if (_renderTick < 0 || MathF.Abs(target - _renderTick) > SimConfig.TICK_RATE)
            _renderTick = target; // desynced (join, big hitch): snap
        else
            _renderTick += (float)delta * SimConfig.TICK_RATE + (target - _renderTick) * 0.05f;

        InterpolatedState? state = _snapshots.Sample(_renderTick);
        if (state == null)
            return;

        int localId = Multiplayer.GetUniqueId();
        HashSet<int> seen = new HashSet<int>();
        _ropes.Segments.Clear();

        foreach (RenderPlayer player in state.Players)
        {
            if (player.PeerId == localId)
                continue;
            seen.Add(player.PeerId);
            Vector2 feet = new Vector2(player.Position.X, player.Position.Y);
            if (_probeWatch)
                ProbeWatch(feet);
            Place(player.PeerId, feet, player.Aim, player.Skin, player.Ammo, player.ReloadTicks);
            if (player.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(player.Position), new Vector2(player.RopePoint.X, player.RopePoint.Y)));
        }

        if (_predictor.Initialized)
        {
            seen.Add(localId);
            _correctionOffset *= MathF.Max(0f, 1f - CORRECTION_DECAY * (float)delta);
            Vector2 pos = new Vector2(_predictor.State.Position.X, _predictor.State.Position.Y);
            Place(localId, pos + _correctionOffset, _localAim, _predictor.State.Skin,
                _predictor.State.Ammo, _predictor.State.ReloadTicks);
            if (_predictor.State.Rope != RopeMode.None)
                _ropes.Segments.Add((
                    BodyCenter(_predictor.State.Position) + _correctionOffset,
                    new Vector2(_predictor.State.RopePoint.X, _predictor.State.RopePoint.Y)));
        }

        foreach ((int peerId, PlayerView? view) in _players)
        {
            if (!seen.Contains(peerId))
            {
                view.QueueFree();
                _players.Remove(peerId);
            }
        }

        PlaceMortars(state.Mortars);
    }

    private void PlaceMortars(IReadOnlyList<RenderMortar> mortars)
    {
        // Everyone else's shells come from snapshots; our own authoritative
        // copies are hidden because the predicted ones below are already on
        // screen (and at present time, not interpolation-delay time).
        int localId = Multiplayer.GetUniqueId();
        HashSet<ushort> seen = new HashSet<ushort>();
        foreach (RenderMortar m in mortars)
        {
            if (m.OwnerId == localId)
                continue;
            seen.Add(m.Id);
            if (!_mortars.TryGetValue(m.Id, out MortarView? view))
            {
                view = new MortarView();
                AddChild(view);
                _mortars[m.Id] = view;
            }
            view.Position = new Vector2(m.Position.X, m.Position.Y);
            view.Rotation = MathF.Atan2(m.Velocity.Y, m.Velocity.X);
        }
        foreach ((ushort id, MortarView view) in _mortars)
        {
            if (!seen.Contains(id))
            {
                view.QueueFree();
                _mortars.Remove(id);
            }
        }

        HashSet<int> seenOwn = new HashSet<int>();
        if (_predictor.Initialized)
        {
            foreach ((int seq, MortarState shell) in _predictor.Shells)
            {
                seenOwn.Add(seq);
                if (!_ownMortars.TryGetValue(seq, out MortarView? view))
                {
                    view = new MortarView();
                    AddChild(view);
                    _ownMortars[seq] = view;
                }
                view.Position = new Vector2(shell.Position.X, shell.Position.Y);
                view.Rotation = MathF.Atan2(shell.Velocity.Y, shell.Velocity.X);
            }
        }
        foreach ((int seq, MortarView view) in _ownMortars)
        {
            if (!seenOwn.Contains(seq))
            {
                view.QueueFree();
                _ownMortars.Remove(seq);
            }
        }
    }

    private void Place(int peerId, Vector2 feet, byte aim, byte skin, byte ammo, byte reloadTicks)
    {
        if (!_players.TryGetValue(peerId, out PlayerView? view))
        {
            view = _playerScene.Instantiate<PlayerView>();
            view.SetIsLocal(peerId == Multiplayer.GetUniqueId());
            AddChild(view);
            _players[peerId] = view;
        }
        view.Apply(feet, aim, skin, ammo, reloadTicks);
    }

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
