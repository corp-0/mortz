using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Replay;
using Mortz.Client.Views;
using Mortz.Core.Net;
using Mortz.Core.Net.Sim;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Net;
using Mortz.Shared.Logging;
using Serilog;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.Client.Match;

[Meta(typeof(IAutoNode))]
public partial class MortarClient : Node,
    IHandle<CarveMsg>,
    IHandle<ShellRetireMsg>,
    IHandle<MortarLifecycleMsg>,
    IHandle<MortarCorrectionMsg>
{
    private static readonly ILogger _log = MortzLog.For("client");

    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private MortarViewManager _views = null!;
    [Export] private FinalKillReplay _finalKillReplay = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency]
    private GameMap Map => this.DependOn<GameMap>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    // Successive parries climb a pentatonic scale, louder each step since
    // pitching up thins the sound.
    private static readonly float[] _parryPitches =
        Array.ConvertAll(new[] { 0, 2, 4, 7, 9, 12 }, st => Mathf.Pow(2f, st / 12f));

    private const float PARRY_GAIN_DB_PER_STEP = 1f;

    private MortarReplicaSet _remoteMortars = null!;
    private Combat _config = null!;
    private MapZones _zones = MapZones.None;
    private Func<int> _newestSnapshotTick = null!;
    private readonly Dictionary<ushort, int> _parriesByMortar = new();

    /// <summary>Must be called before entering the tree.</summary>
    public void Initialize(Combat config, MapZones zones, Func<int> newestSnapshotTick)
    {
        _config = config;
        _zones = zones;
        _newestSnapshotTick = newestSnapshotTick;
    }

    public void OnResolved()
    {
        _remoteMortars = new MortarReplicaSet(Map.Mask, _config, _zones);
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_finalKillReplay.MatchFrozen)
            _remoteMortars.Tick();
    }

    public IReadOnlyList<RenderMortar> RenderFrame()
    {
        IReadOnlyList<RenderMortar> remote = _remoteMortars.Render();
        _views.SyncPredicted(_localPlayer.Shells);
        _views.SyncRemote(remote, _localPlayer.CompletedShells);
        return remote;
    }

    // Retire the predicted copy so it can't fly on and carve a ghost; deflected
    // shells carry -1 and are skipped.
    public void Handle(in CarveMsg msg)
    {
        if (msg.SpawnSeq >= 0 && msg.OwnerId == Network.LocalPeerId &&
            _localPlayer.RetireShell(msg.SpawnSeq))
            _log.Information("retired shell seq {Seq} (authoritative explosion)", msg.SpawnSeq);
    }

    // Reverts the carve too, in case the impact was queued but never reached
    // GameMap; overlaps with the deflect path below, both are idempotent.
    public void Handle(in ShellRetireMsg msg)
    {
        bool hadPrediction = _localPlayer.RetireShell(msg.SpawnSeq);
        bool hadCarve = Map.RevertPredictedCarve(msg.SpawnSeq);
        if (hadPrediction || hadCarve)
            _log.Information("retired shell seq {Seq} (reliable server event)", msg.SpawnSeq);
    }

    public void Handle(in MortarLifecycleMsg msg)
    {
        if (!MortarWire.TryReadLifecycle(msg.Events, out int tick,
                out List<SimWorld.MortarEvent> events))
        {
            _log.Error("dropped malformed mortar lifecycle batch");
            return;
        }
        foreach (SimWorld.MortarEvent e in events)
        {
            switch (e.Kind)
            {
                case SimWorld.MortarEventKind.SPAWN:
                    _remoteMortars.Spawn(e.State, tick, _newestSnapshotTick());
                    if (e.State.FiredBy != Network.LocalPeerId)
                        Sfx.PlayAt(Sfx.Sounds.MortarFire,
                            new Vector2(e.State.Position.X, e.State.Position.Y));
                    break;
                case SimWorld.MortarEventKind.DEFLECT:
                    _remoteMortars.Deflect(e.State, tick, _newestSnapshotTick());
                    RetireDeflectedPrediction(e.State);
                    PlayParrySound(e.State);
                    break;
                case SimWorld.MortarEventKind.END:
                    _parriesByMortar.Remove(e.State.Id);
                    RetireEndedMortar(e.State.Id);
                    break;
            }
        }
    }

    private void PlayParrySound(in MortarState state)
    {
        int step = _parriesByMortar.GetValueOrDefault(state.Id);
        _parriesByMortar[state.Id] = step + 1;
        step = Math.Min(step, _parryPitches.Length - 1);
        Sfx.PlayAt(Sfx.Sounds.ParrySuccess,
            new Vector2(state.Position.X, state.Position.Y),
            _parryPitches[step], step * PARRY_GAIN_DB_PER_STEP);
    }

    private void RetireDeflectedPrediction(in MortarState state)
    {
        if (state.FiredBy != Network.LocalPeerId)
            return;
        bool hadShell = _localPlayer.RetireShell(state.SpawnSeq);
        bool hadCarve = Map.RevertPredictedCarve(state.SpawnSeq);
        if (hadShell || hadCarve)
            _log.Information("retired shell seq {Seq} (deflected)", state.SpawnSeq);
    }

    private void RetireEndedMortar(ushort id)
    {
        if (!_remoteMortars.TryEnd(id, out MortarState state) ||
            state.FiredBy != Network.LocalPeerId)
            return;
        _localPlayer.RetireShell(state.SpawnSeq);
        _localPlayer.ForgetCompleted(state.SpawnSeq);
    }

    public void Handle(in MortarCorrectionMsg msg)
    {
        if (!_remoteMortars.Correct(msg.States, msg.Tick, _newestSnapshotTick()))
            _log.Error("dropped malformed mortar correction");
    }
}
