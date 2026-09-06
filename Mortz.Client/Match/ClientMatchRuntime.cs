using Mortz.Client.Announcements;
using Mortz.Client.Players;
using Mortz.Client.Replication;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Input;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Net.Sim;
using Mortz.Protocol.Replication;

namespace Mortz.Client.Match;

public class ClientMatchRuntime : IDisposable, IHandle<MatchStartMsg>, IHandle<CarveMsg>,
    IHandle<ShellRetireMsg>, IHandle<MortarLifecycleMsg>, IHandle<MortarCorrectionMsg>,
    IHandle<FinalKillMsg>, IHandle<DeathMsg>
{
    private readonly FeatureScope _scope;
    private readonly ClientPlayers _players;
    private readonly int _localPeerId;
    private readonly Action<byte[]> _sendInputs;
    private bool _closed;

    public Func<PlayerInput>? SampleInput { get; set; }
    public AnnouncementDirector Announcements { get; }
    public ClientMatchState State { get; }
    public MatchConfigSnapshot Config { get; }
    public Predictor Predictor { get; }
    public ClientTerrain Terrain { get; }
    public SnapshotInterpolator Interpolator { get; } = new();
    public MortarReplicaSet Mortars { get; }
    public FinalKillMsg? FinalKill { get; private set; }
    public bool Started { get; private set; }
    public bool CanAdvance => !_closed && Started && State.Winner == null && FinalKill == null;
    public Vec2 CorrectionOffset { get; private set; }
    public event Action<Snapshot>? SnapshotApplied;
    public event Action<int, Vec2>? Reconciled;
    public event Action<int>? PacketSent;
    public event Action<SimWorld.MortarEvent>? MortarChanged;
    public event Action<DeathMsg>? Died;
    public event Action<FinalKillMsg>? FinalKillReceived;

    public ClientMatchRuntime(ClientMatchState state, ClientPlayers players,
        TerrainMask terrain, MatchConfig config, MapZones zones, int localPeerId,
        NetRouter router, Action<byte[]> sendInputs, Func<ulong> clock)
    {
        State = state;
        _players = players;
        _localPeerId = localPeerId;
        _sendInputs = sendInputs;
        config = config.ToSnapshot().ToMutable();
        config.Clamp();
        Config = config.ToSnapshot();
        players.OpenMatch(config);
        Predictor = new Predictor(terrain, config, zones);
        Terrain = new ClientTerrain(terrain, config.Combat.MortarCarveRadius, localPeerId, clock);
        Mortars = new MortarReplicaSet(terrain, config.Combat.ToSnapshot().ToMutable(), zones);
        _scope = new FeatureScope(router.Add, router.Remove);
        _scope.Register(new ClientMatchStateAdapter(state));
        Announcements = _scope.Register(new AnnouncementDirector(players, state));
        _scope.Register(this);
        _scope.Own(state.Close);
        _scope.Own(players.CloseMatch);
        players.MatchStatsChanged += OnStatsChanged;
        _scope.Own(() => players.MatchStatsChanged -= OnStatsChanged);
        _scope.Start();
    }

    /// <summary>Seeds a fresh match with full peer IDs before the reliable roster arrives.</summary>
    public bool InitializeSnapshot(byte[] data, int ack)
    {
        if (_closed || Started || Interpolator.NewestTick >= 0)
            return false;
        MatchSnapshot snapshot;
        try
        {
            snapshot = MatchSnapshot.Deserialize(data);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return false;
        }
        if (snapshot.Generation != State.Generation || snapshot.Tick < 0)
            return false;
        ApplySnapshot(snapshot, ack);
        return true;
    }

    public bool AcceptSnapshot(byte[] data, int ack)
    {
        if (_closed)
            return false;
        MatchSnapshot snapshot;
        try
        {
            snapshot = MatchSnapshot.Deserialize(data, _players.Table);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return false;
        }
        if (snapshot.Generation != State.Generation ||
            snapshot.RosterRevision != _players.RosterRevision ||
            snapshot.Tick <= Interpolator.NewestTick)
            return false;
        ApplySnapshot(snapshot, ack);
        return true;
    }

    private void ApplySnapshot(MatchSnapshot snapshot, int ack)
    {
        _players.ApplySnapshot(snapshot);
        Interpolator.Add(snapshot);
        Snapshot simulation = snapshot.SimulationSnapshot;
        foreach (PlayerState player in simulation.Players)
        {
            if (player.PeerId != _localPeerId)
                continue;
            Vec2 correction = Predictor.Reconcile(player, ack, snapshot.Tick);
            CorrectionOffset = correction.Length() > 150f ? Vec2.Zero : CorrectionOffset + correction;
            Reconciled?.Invoke(ack, correction);
            break;
        }
        SnapshotApplied?.Invoke(simulation);
    }

    public void Tick(PlayerInput input)
    {
        if (!CanAdvance)
            return;
        Mortars.Tick();
        if (State.Participation.Seat == MatchSeat.SPECTATOR)
            return;
        if (State.Participation.Activity != MatchActivity.ACTIVE)
            input = input with { Buttons = InputButtons.NONE };
        Predictor.LocalTick(input);
        if (Predictor.NextSeq % NetConfig.TICKS_PER_INPUT_PACKET == 0)
        {
            _sendInputs(InputPacket.Encode(Predictor.RecentInputs(NetConfig.INPUT_REDUNDANCY), State.Generation));
            PacketSent?.Invoke(Predictor.NextSeq - 1);
        }
        foreach ((int sequence, Vec2 position) in Predictor.DrainImpacts())
        {
            Terrain.Predict(sequence, position);
        }
    }

    public void Advance(float delta)
    {
        if (_closed)
            return;
        Terrain.Advance();
        foreach (IClientFrameFeature feature in _scope.Implementing<IClientFrameFeature>())
        {
            feature.Advance(delta);
        }
        CorrectionOffset *= MathF.Max(0f, 1f - 10f * delta);
    }

    private bool Accepts(int generation) => !_closed && generation == State.Generation;

    public void Handle(in MatchStartMsg message)
    {
        if (Accepts(message.Generation))
            Started = true;
    }

    public void Handle(in CarveMsg message)
    {
        if (!Accepts(message.MatchGeneration))
            return;
        if (message.OwnerId == _localPeerId && message.SpawnSeq >= 0)
            Predictor.RetireShell(message.SpawnSeq);
        Terrain.Apply(message);
    }

    public void Handle(in ShellRetireMsg message)
    {
        if (!Accepts(message.MatchGeneration))
            return;
        Predictor.RetireShell(message.SpawnSeq);
        Terrain.Retire(message.SpawnSeq);
    }

    public void Handle(in MortarLifecycleMsg message)
    {
        if (!Accepts(message.MatchGeneration) ||
            !MortarWire.TryReadLifecycle(message.Events, out int tick, out List<SimWorld.MortarEvent> events))
            return;
        foreach (SimWorld.MortarEvent change in events)
        {
            MortarState shell = change.State;
            switch (change.Kind)
            {
                case SimWorld.MortarEventKind.SPAWN:
                    Mortars.Spawn(shell, tick, Interpolator.NewestTick);
                    break;
                case SimWorld.MortarEventKind.DEFLECT:
                    Mortars.Deflect(shell, tick, Interpolator.NewestTick);
                    if (shell.FiredBy == _localPeerId)
                    {
                        Predictor.RetireShell(shell.SpawnSeq);
                        Terrain.Retire(shell.SpawnSeq);
                    }
                    break;
                case SimWorld.MortarEventKind.END:
                    if (Mortars.TryEnd(shell.Id, out MortarState ended) && ended.FiredBy == _localPeerId)
                    {
                        Predictor.RetireShell(ended.SpawnSeq);
                        Predictor.ForgetCompleted(ended.SpawnSeq);
                    }
                    break;
            }
            MortarChanged?.Invoke(change);
        }
    }

    public void Handle(in MortarCorrectionMsg message)
    {
        if (Accepts(message.MatchGeneration))
            Mortars.Correct(message.States, message.Tick, Interpolator.NewestTick);
    }

    public void Handle(in FinalKillMsg message)
    {
        if (!Accepts(message.MatchGeneration) || FinalKill != null)
            return;
        FinalKill = message;
        FinalKillReceived?.Invoke(message);
    }

    public void Handle(in DeathMsg message)
    {
        if (Accepts(message.MatchGeneration))
            Died?.Invoke(message);
    }

    private void OnStatsChanged(ClientPlayer player)
    {
        if (player.PeerId == _localPeerId && player.Match != null)
            Predictor.SetModifiers(player.Match.Modifiers, player.Match.ModifierEffectiveTick,
                player.Match.ModifierRevision);
    }

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _scope.Dispose();
    }
}
