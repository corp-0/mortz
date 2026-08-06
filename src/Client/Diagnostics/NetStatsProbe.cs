using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Match;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Diagnostics;

/// <summary>
/// --net-stats: one line per second of client netcode numbers, tapped off the
/// local player controller and the interpolation clock through their
/// diagnostics events. Inert without the flag.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class NetStatsProbe : Node
{
    private static readonly ILogger _log = MortzLog.For("stats");

    [Export] private GameView _gameView = null!;
    [Export] private LocalPlayerController _localPlayer = null!;

    [Dependency]
    private NetworkManager Network => this.DependOn<NetworkManager>();

    public override void _Notification(int what) => this.Notify(what);

    private readonly Dictionary<int, ulong> _inputSendTimes = new();
    private int _frames;
    private int _lastAckSeen = -1;
    private ulong _lastSnapshotMsec;
    private double _rttSum, _rttMax;
    private int _rttCount;
    private float _corrSum;
    private int _corrCount;

    public override void _Ready()
    {
        if (!CmdArgs.HasFlag("--net-stats"))
        {
            SetPhysicsProcess(false);
            return;
        }
        _localPlayer.PacketSent += OnPacketSent;
        _localPlayer.Reconciled += OnReconciled;
    }

    private void OnPacketSent(int seq) => _inputSendTimes[seq] = Time.GetTicksMsec();

    private void OnReconciled(int ack, Vec2 correction)
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
        {
            if (seq <= ack)
                stale.Add(seq);
        }
        foreach (int seq in stale)
        {
            _inputSendTimes.Remove(seq);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++_frames < SimConfig.TICK_RATE)
            return;
        _frames = 0;
        (double sent, double recv, double sentPk, double recvPk) = Network.PopWireStats();
        int newest = _gameView.NewestSnapshotTick;
        float interpTicks = newest >= 0 ? newest - _gameView.RenderTick : -1;
        double snapAge = _lastSnapshotMsec > 0 ? Time.GetTicksMsec() - _lastSnapshotMsec : -1;
        double rttAvg = _rttCount > 0 ? _rttSum / _rttCount : -1;
        float corrAvg = _corrCount > 0 ? _corrSum / _corrCount : 0;
        _log.Information(
            "unix={Unix:F3} seq={Seq} newest={Newest} renderTick={RenderTick:F1} " +
            "interp={Interp:F1}tk snapAge={SnapAge:F0}ms rtt={RttAvg:F0}avg/{RttMax:F0}max ms " +
            "corr={Correction:F2}px up={SentBytes:F0}B/{SentPackets:F0}pk " +
            "down={RecvBytes:F0}B/{RecvPackets:F0}pk",
            Time.GetUnixTimeFromSystem(), _localPlayer.NextSeq, newest, _gameView.RenderTick,
            interpTicks, snapAge, rttAvg, _rttMax, corrAvg, sent, sentPk, recv, recvPk);
        _rttSum = 0; _rttMax = 0; _rttCount = 0;
        _corrSum = 0; _corrCount = 0;
    }
}
