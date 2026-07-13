using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Shared;

namespace Mortz.Client.Diagnostics;

/// <summary>
/// Headless E2E hooks: the always-on once-per-sim-second snapshot heartbeat
/// and score/match-end echoes, --test-fire (hold fire pointing at the floor:
/// exercises explosion and self-death) and --test-carve (one debug carve
/// request at the first destructible spot in the map).
/// </summary>
public partial class E2eHooks : Node
{
    [Export] private GameView _gameView = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private GameMap _gameMap = null!;

    private int _lastLoggedSecond = -1;
    private bool _testCarveSent;

    public override void _Ready()
    {
        _gameView.SnapshotApplied += LogHeartbeat;
        ScoreMsg.Received += LogScore;
        MatchEndMsg.Received += LogMatchEnd;
        if (CmdArgs.HasFlag("--test-fire"))
        {
            _localPlayer.ButtonFilter = (_, buttons) => buttons | InputButtons.Fire;
            _localPlayer.AimOverride = 64; // straight down: point-blank floor shots
        }
        if (CmdArgs.HasFlag("--test-carve"))
            _localPlayer.Reconciled += SendTestCarveOnce;
    }

    public override void _ExitTree()
    {
        ScoreMsg.Received -= LogScore;
        MatchEndMsg.Received -= LogMatchEnd;
    }

    /// <summary>Score echoes prove the wire end to end until the kill feed UI
    /// exists; peer ids, not names, this side stays dumb.</summary>
    private static void LogScore(ScoreMsg m)
    {
        bool suicide = m.KillerId == 0 || m.KillerId == m.VictimId;
        GD.Print(suicide
            ? $"[client] score: {m.VictimId} suicides ({m.KillerKills} kills, {m.VictimDeaths} deaths)"
            : $"[client] score: {m.KillerId} killed {m.VictimId} ({m.KillerKills} kills), " +
              $"teams {m.Team1Kills}-{m.Team2Kills}");
    }

    private static void LogMatchEnd(MatchEndMsg m) =>
        GD.Print($"[client] match over: {(m.ByTeam ? $"team {m.WinnerId}" : $"player {m.WinnerId}")} wins");

    /// <summary>Heartbeat log for headless E2E verification, once per sim-second.</summary>
    private void LogHeartbeat(Snapshot snapshot)
    {
        int second = snapshot.Tick / SimConfig.TICK_RATE;
        if (second == _lastLoggedSecond)
            return;
        _lastLoggedSecond = second;
        GD.Print($"[client] snapshot tick {snapshot.Tick}, {snapshot.Players.Length} player(s)");
    }

    private void SendTestCarveOnce(int ack, Vec2 correction)
    {
        if (_testCarveSent)
            return;
        _testCarveSent = true;
        for (int y = 0; y < _gameMap.Mask.Height; y++)
            for (int x = 0; x < _gameMap.Mask.Width; x++)
            {
                if (_gameMap.Mask.Get(x, y) == TerrainMaterial.Destructible)
                {
                    GD.Print($"[client] requesting test carve at ({x},{y})");
                    new DebugCarveMsg(x, y).SendToServer();
                    return;
                }
            }
    }
}
