using Godot;
using Mortz.Client.Match;
using Mortz.Client.Views;
using Mortz.Core.Sim;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Diagnostics;

/// <summary>
/// Perceived-lag probe, run as a pair of clients on one machine (shared wall
/// clock): --probe-input injects walking bursts and logs the press edge,
/// --probe-watch logs when a remote player's rendered position first moves
/// after being still. Timestamp difference = true press-to-screen lag.
/// </summary>
public partial class LagProbes : Node
{
    private static readonly ILogger _log = MortzLog.For("probe");

    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private PlayerViewManager _players = null!;

    // --probe-watch state: last stable position and whether we're waiting for it to move.
    private Vector2 _reference;
    private ulong _stableSince;
    private bool _armed;

    public override void _Ready()
    {
        if (CmdArgs.HasFlag("--probe-input"))
            _localPlayer.ButtonFilter = ProbeButtons;
        if (CmdArgs.HasFlag("--probe-watch"))
            _players.RemotePlaced += ProbeWatch;
    }

    /// <summary>Half a second of walking every five seconds, alternating
    /// direction so the player ends up roughly where it started.</summary>
    private static InputButtons ProbeButtons(int seq, InputButtons _)
    {
        const int CYCLE = 5 * SimConfig.TICK_RATE;
        int phase = seq % CYCLE;
        if (phase >= SimConfig.TICK_RATE / 2)
            return InputButtons.NONE;
        if (phase == 0)
            _log.Information("press unix={Unix:F3}", Time.GetUnixTimeFromSystem());
        return (seq / CYCLE) % 2 == 0 ? InputButtons.RIGHT : InputButtons.LEFT;
    }

    /// <summary>Logs when a remote player's rendered position first moves
    /// after a second of standing still. Runs on the interpolated output, so
    /// it sees exactly what the screen shows.</summary>
    private void ProbeWatch(Vector2 pos)
    {
        ulong now = Time.GetTicksMsec();
        if (_armed)
        {
            if ((pos - _reference).Length() > 0.3f)
            {
                _log.Information("move unix={Unix:F3}", Time.GetUnixTimeFromSystem());
                _armed = false;
                _reference = pos;
                _stableSince = now;
            }
            return;
        }
        if ((pos - _reference).Length() > 0.01f)
        {
            _reference = pos;
            _stableSince = now;
        }
        else if (now - _stableSince > 1000)
            _armed = true;
    }
}
