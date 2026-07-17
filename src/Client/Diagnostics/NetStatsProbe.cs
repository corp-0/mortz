using Godot;
using Mortz.Client.Match;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client.Diagnostics;

/// <summary>
/// --net-stats: one line per second of client netcode numbers, tapped off the
/// local player controller and the interpolation clock through their
/// diagnostics events. Inert without the flag.
/// </summary>
public partial class NetStatsProbe : Node
{
    [Export] private GameView _gameView = null!;
    [Export] private LocalPlayerController _localPlayer = null!;

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
        (double sent, double recv, double sentPk, double recvPk) = NetworkManager.Instance.PopWireStats();
        int newest = _gameView.NewestSnapshotTick;
        float interpTicks = newest >= 0 ? newest - _gameView.RenderTick : -1;
        double snapAge = _lastSnapshotMsec > 0 ? Time.GetTicksMsec() - _lastSnapshotMsec : -1;
        double rttAvg = _rttCount > 0 ? _rttSum / _rttCount : -1;
        float corrAvg = _corrCount > 0 ? _corrSum / _corrCount : 0;
        GD.Print($"[stats] unix={Time.GetUnixTimeFromSystem():F3} seq={_localPlayer.NextSeq} " +
                 $"newest={newest} renderTick={_gameView.RenderTick:F1} interp={interpTicks:F1}tk " +
                 $"snapAge={snapAge:F0}ms rtt={rttAvg:F0}avg/{_rttMax:F0}max ms corr={corrAvg:F2}px " +
                 $"up={sent:F0}B/{sentPk:F0}pk down={recv:F0}B/{recvPk:F0}pk");
        _rttSum = 0; _rttMax = 0; _rttCount = 0;
        _corrSum = 0; _corrCount = 0;
    }
}
