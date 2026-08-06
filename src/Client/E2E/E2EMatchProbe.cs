#if TOOLS
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Match;
using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.E2E;

/// <summary>
/// Hands the live match to whatever bridge the client root provides, and keeps
/// the plain-text heartbeat and score echoes that make an artifact log
/// readable. Log text is diagnostics only; assertions ride the protocol.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class E2EMatchProbe : Node,
    IHandle<EliminationMsg>,
    IHandle<MatchEndMsg>
{
    private static readonly ILogger _log = MortzLog.For("client");

    private GameView _gameView = null!;
    private LocalPlayerController _localPlayer = null!;

    [Dependency]
    private IE2EClientBridge Bridge => this.DependOn<IE2EClientBridge>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    private int _lastLoggedSecond = -1;

    public override void _Notification(int what) => this.Notify(what);

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(GameView gameView, LocalPlayerController localPlayer)
    {
        _gameView = gameView;
        _localPlayer = localPlayer;
    }

    public void OnResolved()
    {
        _gameView.SnapshotApplied += LogHeartbeat;
        _routed = Router;
        _routed.Add(this);
        Bridge.AttachMatch(_gameView, _localPlayer);
    }

    public void OnExitTree()
    {
        if (_routed == null)
            return;
        _gameView.SnapshotApplied -= LogHeartbeat;
        _routed.Remove(this);
        _routed = null;
        Bridge.DetachMatch();
    }

    /// <summary>Score echoes for the artifact log; peer ids, not names, this
    /// side stays dumb.</summary>
    public void Handle(in EliminationMsg m)
    {
        bool suicide = m.Flags.HasFlag(EliminationFlags.SUICIDE);
        if (suicide)
        {
            _log.Information("score: {VictimId} suicides ({Kills} kills, {Deaths} deaths)",
                m.VictimId, m.KillerKills, m.VictimDeaths);
        }
        else
        {
            _log.Information("score: {KillerId} killed {VictimId} ({Kills} kills), " +
                             "teams {BlueKills}-{RedKills}",
                m.KillerId, m.VictimId, m.KillerKills, m.BlueKills, m.RedKills);
        }
    }

    public void Handle(in MatchEndMsg message)
    {
        if (!MatchProtocol.TryDecode(message, out Victor? winner))
            return;
        string who = winner switch
        {
            Victor.Team team => Teams.Name(team.Value),
            Victor.Player player => $"player {player.PeerId}",
            _ => "nobody",
        };
        _log.Information("match over: {Winner} wins", who);
    }

    /// <summary>Heartbeat log for headless verification, once per sim-second.</summary>
    private void LogHeartbeat(Snapshot snapshot)
    {
        int second = snapshot.Tick / SimConfig.TICK_RATE;
        if (second == _lastLoggedSecond)
            return;
        _lastLoggedSecond = second;
        _log.Information("snapshot tick {Tick}, {Players} player(s)", snapshot.Tick,
            snapshot.Players.Length);
    }
}
#endif
