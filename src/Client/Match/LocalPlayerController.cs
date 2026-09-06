using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Replication;
using Mortz.Core.Match.Participation;
using Mortz.Core.Sim;

namespace Mortz.Client.Match;

/// <summary>Samples local input and exposes prediction for presentation and diagnostics.</summary>
[Meta(typeof(IAutoNode))]
public partial class LocalPlayerController : Node2D
{
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

    private ClientMatchRuntime _runtime = null!;
    private Predictor _predictor => _runtime.Predictor;
    private byte _aim;
    private MatchParticipation _participation = MatchParticipation.Active;

    [Dependency]
    private ClientMatchState MatchState => this.DependOn<ClientMatchState>();

    /// <summary>False until the first snapshot containing the local player arrives.</summary>
    public bool Initialized => _predictor.Initialized;
    public PlayerState State => _predictor.State;
    public int NextSeq => _predictor.NextSeq;
    public byte Aim => _aim;
    public Vector2 CorrectionOffset => new(_runtime.CorrectionOffset.X, _runtime.CorrectionOffset.Y);
    public IReadOnlyList<(int SpawnSeq, MortarState Shell)> Shells => _predictor.Shells;
    public bool Frozen => !_runtime.CanAdvance;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(ClientMatchRuntime runtime)
    {
        _runtime = runtime;
        runtime.SampleInput = Sample;
        runtime.PacketSent += OnPacketSent;
        runtime.Reconciled += OnReconciled;
    }

    private void OnPacketSent(int sequence) => PacketSent?.Invoke(sequence);
    private void OnReconciled(int ack, Vec2 correction) => Reconciled?.Invoke(ack, correction);

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _participation = MatchState.Participation;
        MatchState.ParticipationChanged += OnParticipationChanged;
    }

    public void OnExitTree()
    {
        MatchState.ParticipationChanged -= OnParticipationChanged;
        if (_runtime == null)
            return;
        _runtime.SampleInput = null;
        _runtime.PacketSent -= OnPacketSent;
        _runtime.Reconciled -= OnReconciled;
    }

    private PlayerInput Sample()
    {
        if (Frozen || _participation.Seat == MatchSeat.SPECTATOR)
            return default;

        InputButtons buttons = _participation.Activity == MatchActivity.ACTIVE
            ? InputSampler.Sample()
            : InputButtons.NONE;

        if (_predictor.Initialized && _participation.Activity == MatchActivity.ACTIVE)
        {
            Vector2 toMouse = GetGlobalMousePosition() - BodyCenter();
            if (toMouse.LengthSquared() > 1)
                _aim = PlayerInput.AimFromVector(new Vec2(toMouse.X, toMouse.Y));
        }

        if (ButtonFilter != null)
            buttons = ButtonFilter(_predictor.NextSeq, buttons);
        if (AimOverride is byte aim)
            _aim = aim;
        if (AimProvider != null)
            _aim = AimProvider();

        return new PlayerInput(buttons, _aim);
    }

    public IReadOnlySet<int> CompletedShells => _predictor.CompletedShells;

    private void OnParticipationChanged(MatchParticipation participation) =>
        _participation = participation;

    private Vector2 BodyCenter() =>
        new(State.Position.X, State.Position.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
