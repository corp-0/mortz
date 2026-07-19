using Godot;
using Mortz.Client.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client.Diagnostics;

/// <summary>
/// Headless E2E hooks: the always-on once-per-sim-second snapshot heartbeat
/// and score/match-end echoes, --test-fire (hold fire pointing at the floor:
/// exercises explosion and self-death), --test-carve (one debug carve request
/// at the first destructible spot in the map), --test-hunt (seek the nearest
/// enemy and lob shells at it) and --test-parry (pulse the parry bubble). Hunt
/// plus parry on both clients drives direct hits and deflections, the two ways
/// the server ends a shell early: exactly what the shell-retirement path needs.
/// </summary>
public partial class E2EHooks : Node
{
    [Export] private GameView _gameView = null!;
    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private GameMap _gameMap = null!;

    private int _lastLoggedSecond = -1;
    private bool _testCarveSent;
    private Vec2 _targetFeet;
    private bool _hasTarget;

    public override void _Ready()
    {
        _gameView.SnapshotApplied += LogHeartbeat;
        EliminationMsg.Received += LogScore;
        MatchEndMsg.Received += LogMatchEnd;
        if (CmdArgs.HasFlag("--test-fire"))
        {
            _localPlayer.ButtonFilter = (_, buttons) => buttons | InputButtons.FIRE;
            _localPlayer.AimOverride = 64; // straight down: point-blank floor shots
        }
        if (CmdArgs.HasFlag("--test-carve"))
            _localPlayer.Reconciled += SendTestCarveOnce;

        bool hunt = CmdArgs.HasFlag("--test-hunt");
        bool parry = CmdArgs.HasFlag("--test-parry");
        if (hunt || parry)
            _localPlayer.ButtonFilter = (seq, buttons) =>
            {
                if (hunt)
                {
                    buttons |= HuntMove();
                    if (seq % 15 < 3) buttons |= InputButtons.FIRE;     // ~4 shots/sec
                }
                if (parry && seq % 40 < 3) buttons |= InputButtons.PARRY; // raise the bubble often
                return buttons;
            };
        if (hunt)
        {
            _localPlayer.AimProvider = HuntAim;
            _gameView.SnapshotApplied += TrackNearestEnemy;
        }
    }

    private void TrackNearestEnemy(Snapshot snapshot)
    {
        int localId = NetworkManager.Instance.LocalPeerId;
        Vec2 me = _localPlayer.State.Position;
        float best = float.MaxValue;
        foreach (PlayerState p in snapshot.Players)
        {
            if (p.PeerId == localId)
                continue;
            float d = (p.Position - me).LengthSquared();
            if (d < best)
            {
                best = d;
                _targetFeet = p.Position;
                _hasTarget = true;
            }
        }
    }

    /// <summary>Lob at the tracked enemy: raise the aim to cancel the drop over
    /// the flight, so shots land from a safe stand-off and arc over low cover.</summary>
    private byte HuntAim()
    {
        if (!_hasTarget)
            return _localPlayer.Aim;
        Vec2 from = BodyCenter(_localPlayer.State.Position);
        Vec2 to = BodyCenter(_targetFeet);
        float t = (to.X - from.X) / SimConfig.MORTAR_SPEED;
        float drop = 0.5f * SimConfig.MORTAR_GRAVITY * t * t;
        Vec2 dir = new Vec2(to.X - from.X, (to.Y - drop) - from.Y);
        return dir.LengthSquared() < 1 ? _localPlayer.Aim : PlayerInput.AimFromVector(dir);
    }

    /// <summary>Kite to a stand-off distance so our own blast (and the deflected
    /// shell coming back) stays off us while we feed shells to the bubble.</summary>
    private InputButtons HuntMove()
    {
        if (!_hasTarget)
            return InputButtons.NONE;
        const float STANDOFF = 240f, BAND = 50f;
        float dx = _targetFeet.X - _localPlayer.State.Position.X;
        float adist = MathF.Abs(dx);
        if (adist > STANDOFF + BAND)
            return dx > 0 ? InputButtons.RIGHT : InputButtons.LEFT;  // approach
        if (adist < STANDOFF - BAND)
            return dx > 0 ? InputButtons.LEFT : InputButtons.RIGHT;  // back off
        return InputButtons.NONE;
    }

    private static Vec2 BodyCenter(Vec2 feet) => feet with { Y = feet.Y - SimConfig.PLAYER_HALF_HEIGHT };

    public override void _ExitTree()
    {
        EliminationMsg.Received -= LogScore;
        MatchEndMsg.Received -= LogMatchEnd;
    }

    /// <summary>Score echoes for E2E log matching; peer ids, not names, this
    /// side stays dumb.</summary>
    private static void LogScore(EliminationMsg m)
    {
        bool suicide = (m.Flags & EliminationFlags.SUICIDE) != 0;
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
        {
            for (int x = 0; x < _gameMap.Mask.Width; x++)
            {
                if (_gameMap.Mask.Get(x, y) == TerrainMaterial.DESTRUCTIBLE)
                {
                    GD.Print($"[client] requesting test carve at ({x},{y})");
                    new DebugCarveMsg(x, y).SendToServer();
                    return;
                }
            }
        }
    }
}
