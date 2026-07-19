using Godot;
using Mortz.Core.Input;
using Mortz.Core.Net;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Net;

namespace Mortz.Client.Match;

/// <summary>
/// The locally predicted player: samples input every sim tick, feeds the
/// Predictor, ships input packets to the server and reconciles the prediction
/// against incoming snapshots. Small corrections ease in over a few frames
/// (CorrectionOffset), teleport-scale ones snap.
/// </summary>
public partial class LocalPlayerController : Node2D
{
    /// <summary>How fast reconciliation corrections blend away (per second).</summary>
    private const float CORRECTION_DECAY = 10f;
    /// <summary>Corrections beyond this (respawn after a death pit) snap
    /// immediately instead of sliding across the map.</summary>
    private const float SNAP_DISTANCE = 150f;

    /// <summary>Diagnostics tap: an input packet went out; the newest seq it carries.</summary>
    public event Action<int>? PacketSent;
    /// <summary>Diagnostics tap: reconciled against a snapshot (ack, correction).</summary>
    public event Action<int, Vec2>? Reconciled;

    /// <summary>Debug/E2E hook: rewrites the sampled buttons before they enter prediction.</summary>
    public Func<int, InputButtons, InputButtons>? ButtonFilter { get; set; }
    /// <summary>Debug/E2E hook: pins the aim regardless of the mouse.</summary>
    public byte? AimOverride { get; set; }
    /// <summary>Debug/E2E hook: recomputes the aim each tick (e.g. seek an enemy).</summary>
    public Func<byte>? AimProvider { get; set; }

    private Predictor _predictor = null!;
    private Vector2 _correctionOffset;
    private byte _aim;

    /// <summary>False until the first snapshot containing the local player arrives.</summary>
    public bool Initialized => _predictor.Initialized;
    public PlayerState State => _predictor.State;
    public int NextSeq => _predictor.NextSeq;
    public byte Aim => _aim;
    public Vector2 CorrectionOffset => _correctionOffset;
    public IReadOnlyList<(int SpawnSeq, MortarState Shell)> Shells => _predictor.Shells;
    public bool Frozen { get; set; }

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(Predictor predictor) => _predictor = predictor;

    public override void _PhysicsProcess(double delta)
    {
        if (Frozen)
            return;

        InputButtons buttons = InputSampler.Sample();

        if (_predictor.Initialized)
        {
            Vector2 toMouse = GetGlobalMousePosition() - BodyCenter();
            if (toMouse.LengthSquared() > 1)
                _aim = PlayerInput.AimFromVector(new Vec2(toMouse.X, toMouse.Y));
        }

        if (ButtonFilter != null)
            buttons = ButtonFilter(_predictor.NextSeq, buttons);
        if (AimOverride is { } aim)
            _aim = aim;
        if (AimProvider != null)
            _aim = AimProvider();

        _predictor.LocalTick(new PlayerInput(buttons, _aim));
        if (_predictor.NextSeq % NetConfig.TICKS_PER_INPUT_PACKET == 0)
        {
            NetworkManager.Instance.SendInputs(
                InputPacket.Encode(_predictor.RecentInputs(NetConfig.INPUT_REDUNDANCY)));
            PacketSent?.Invoke(_predictor.NextSeq - 1);
        }
    }

    public override void _Process(double delta) =>
        _correctionOffset *= MathF.Max(0f, 1f - CORRECTION_DECAY * (float)delta);

    /// <summary>Rewind-and-replay against the authoritative state, if the local player is in it.</summary>
    public void Reconcile(Snapshot snapshot, int ack)
    {
        int localId = NetworkManager.Instance.LocalPeerId;
        foreach (PlayerState player in snapshot.Players)
        {
            if (player.PeerId != localId)
                continue;
            if (!_predictor.Initialized)
                GD.Print("[client] prediction initialized");
            Vec2 correction = _predictor.Reconcile(player, ack, snapshot.Tick);
            if (correction.Length() > SNAP_DISTANCE)
                _correctionOffset = Vector2.Zero;
            else
                _correctionOffset += new Vector2(correction.X, correction.Y);
            Reconciled?.Invoke(ack, correction);
            break;
        }
    }

    /// <summary>Predicted terrain impacts since the last drain, for predicted carving.</summary>
    public List<(int SpawnSeq, Vec2 Position)> DrainImpacts() => _predictor.DrainImpacts();

    /// <summary>Retire one of our shells the server ended early; true if it was still flying.</summary>
    public bool RetireShell(int spawnSeq) => _predictor.RetireShell(spawnSeq);

    /// <summary>Shots the owner already watched end; their late authoritative copies stay hidden.</summary>
    public IReadOnlySet<int> CompletedShells => _predictor.CompletedShells;

    /// <summary>The authoritative shell ended; its seq no longer needs hiding.</summary>
    public void ForgetCompleted(int spawnSeq) => _predictor.ForgetCompleted(spawnSeq);

    /// <summary>True if a predicted shell for this seq is still live.</summary>
    public bool HasPredictedShell(int spawnSeq) => _predictor.HasShell(spawnSeq);

    private Vector2 BodyCenter() =>
        new(State.Position.X, State.Position.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
