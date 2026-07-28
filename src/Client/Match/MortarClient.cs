using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Replay;
using Mortz.Client.Views;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Net;

namespace Mortz.Client.Match;

[Meta(typeof(IAutoNode))]
public partial class MortarClient : Node
{
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private MortarViewManager _views = null!;
    [Export] private FinalKillReplay _finalKillReplay = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency]
    private GameMap Map => this.DependOn<GameMap>();

    public override void _Notification(int what) => this.Notify(what);

    // Successive parries climb a pentatonic scale, louder each step since
    // pitching up thins the sound.
    private static readonly float[] _parryPitches =
        Array.ConvertAll(new[] { 0, 2, 4, 7, 9, 12 }, st => Mathf.Pow(2f, st / 12f));

    private const float PARRY_GAIN_DB_PER_STEP = 1f;

    private MortarReplicaSet _remoteMortars = null!;
    private MatchConfig _config = null!;
    private Func<int> _newestSnapshotTick = null!;
    private readonly Dictionary<ushort, int> _parriesByMortar = new();

    /// <summary>Must be called before entering the tree.</summary>
    public void Initialize(MatchConfig config, Func<int> newestSnapshotTick)
    {
        _config = config;
        _newestSnapshotTick = newestSnapshotTick;
    }

    public void OnResolved()
    {
        _remoteMortars = new MortarReplicaSet(Map.Mask, _config);
        CarveMsg.Received += OnCarve;
        ShellRetireMsg.Received += OnShellRetire;
        MortarLifecycleMsg.Received += OnMortarLifecycle;
        MortarCorrectionMsg.Received += OnMortarCorrection;
    }

    public void OnExitTree()
    {
        CarveMsg.Received -= OnCarve;
        ShellRetireMsg.Received -= OnShellRetire;
        MortarLifecycleMsg.Received -= OnMortarLifecycle;
        MortarCorrectionMsg.Received -= OnMortarCorrection;
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

    // Retire the predicted copy so it can't fly on and carve a ghost.
    // Deflected shells carry -1 and are skipped.
    private void OnCarve(CarveMsg msg)
    {
        if (msg.SpawnSeq >= 0 && msg.OwnerId == Network.LocalPeerId &&
            _localPlayer.RetireShell(msg.SpawnSeq))
            GD.Print($"[client] retired shell seq {msg.SpawnSeq} (authoritative explosion)");
    }

    // Reverts the carve too, in case the impact was queued but never reached
    // GameMap. Overlaps with the deflect path below; both are idempotent.
    private void OnShellRetire(ShellRetireMsg msg)
    {
        bool hadPrediction = _localPlayer.RetireShell(msg.SpawnSeq);
        bool hadCarve = Map.RevertPredictedCarve(msg.SpawnSeq);
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
            GD.Print($"[client] retired shell seq {state.SpawnSeq} (deflected)");
    }

    private void RetireEndedMortar(ushort id)
    {
        if (!_remoteMortars.TryEnd(id, out MortarState state) ||
            state.FiredBy != Network.LocalPeerId)
            return;
        _localPlayer.RetireShell(state.SpawnSeq);
        _localPlayer.ForgetCompleted(state.SpawnSeq);
    }

    private void OnMortarCorrection(MortarCorrectionMsg msg)
    {
        if (!_remoteMortars.Correct(msg.States, msg.Tick, _newestSnapshotTick()))
            GD.PrintErr("[client] dropped malformed mortar correction");
    }
}
