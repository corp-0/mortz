using Mortz.Core.Input;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Server.Content;
using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Mortz.Server.Wins;
using Serilog;

namespace Mortz.Server.Phases;

/// <summary>Everything whose lifetime is exactly one match.</summary>
public sealed class MatchPhase : ServerPhase
{
    private const float MATCH_END_SECONDS = 7;

    private readonly MatchRuntime _runtime;
    private readonly MatchStateKeys _keys;
    private readonly MatchReplication _replication;
    private readonly WinsService _wins;
    private readonly MapSnapshot _map;
    private readonly ILogger _log;
    private readonly IMatchObserver _observer;
    private readonly IMatchControl _control;
    private readonly bool _allowJoinInProgress;
    private readonly IReadOnlyList<SeatAssignment> _seats;
    private readonly object[] _services;

    private MatchPhase(MatchRuntime runtime, MatchStateKeys keys,
        MatchReplication replication, WinsService wins, MapSnapshot map, ILogger log,
        IMatchObserver observer, IMatchControl control, bool allowJoinInProgress,
        IReadOnlyList<SeatAssignment> seats)
    {
        _runtime = runtime;
        _keys = keys;
        _replication = replication;
        _wins = wins;
        _map = map;
        _log = log;
        _observer = observer;
        _control = control;
        _allowJoinInProgress = allowJoinInProgress;
        _seats = seats;
        _services = [_replication];
    }

    public override ServerPhaseKind Kind => ServerPhaseKind.MATCH;

    public override IReadOnlyList<object> Services => _services;

    public static MatchPhase Open(IReadOnlyList<SeatAssignment> seats,
        SettingsService settings, WinsService wins, Roster roster, IServerLink link,
        ILogger log, bool netStats, int generation, IMatchObserver observer,
        IMatchControl control, bool allowJoinInProgress)
    {
        MapSnapshot map = settings.Map;
        int victoryLapTicks = (int)(MATCH_END_SECONDS * SimConfig.TICK_RATE);
        MatchStateKeys keys = new(generation);
        MatchRuntime runtime = new(map.BuildMask(), settings.Config, victoryLapTicks, keys,
            map.SpawnPoints, map.Zones);
        MatchReplication replication = new(runtime, roster, map, link, log, netStats);
        return new MatchPhase(runtime, keys, replication, wins, map, log, observer, control,
            allowJoinInProgress, seats);
    }

    public override void OpenPhaseKeys(Player player) =>
        player.OpenMatch(_keys.Count, _keys.Generation);

    /// <summary>Seating writes match state, so it must wait until every roster
    /// player has it open.</summary>
    public override void Begin()
    {
        foreach (SeatAssignment seat in _seats)
        {
            Seat(seat.Player, seat.Team);
        }
        _replication.BroadcastRoster();
    }

    public override void PlayerJoined(Player player)
    {
        if (_allowJoinInProgress)
        {
            Seat(player, null);
        }
        else
        {
            _runtime.AddJipSpectator(player);
            _log.Information("player {PeerId} joined as spectator", player.PeerId);
        }
        _replication.BroadcastRoster();
    }

    public override void Load(Player player, int generation, bool initialPhase) =>
        _replication.Enter(player, generation, initialPhase);

    public override void PlayerLeft(Player player)
    {
        _runtime.Remove(player);
        _log.Information("player {PeerId} left ({InGame} in game)", player.PeerId,
            _runtime.World.Players.Count);
        _replication.BroadcastRoster();
    }

    public override void Inputs(Player player, byte[] packet)
    {
        if (!InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> inputs))
            return;
        _replication.RecordInputPayload(packet.Length);
        foreach ((int sequence, PlayerInput input) in inputs)
        {
            _runtime.EnqueueInput(player, sequence, input);
        }
    }

    public override PhaseRequest Advance(ServerTime time)
    {
        _control.ApplyBefore(_runtime.World);
        MatchUpdate update = _runtime.Advance(time);
        _control.CompleteAfter(_runtime.World);
        _replication.Publish(update, time);
        if (update.MatchEnded is Victor winner)
        {
            _wins.Record(_runtime.Winners(winner));
        }
        _observer.MatchAdvanced(update);
        return update.ReturnToLobby ? PhaseRequest.RETURN_TO_LOBBY : PhaseRequest.NONE;
    }

    public override void Dispose() => _runtime.Dispose();

    private void Seat(Player player, Team? lobbyTeam)
    {
        if (_map.SpawnPoints.Length > 0 &&
            _runtime.World.Players.Count >= _map.SpawnPoints.Length)
        {
            _log.Warning(
                "map '{MapId}' only has {Spawns} spawn point(s) for {Players} players, " +
                "so some will share one",
                _map.MapId, _map.SpawnPoints.Length, _runtime.World.Players.Count + 1);
        }
        Team? team = _runtime.Seat(player, lobbyTeam);
        string onTeam = team is Team assigned ? $" on {Teams.Name(assigned)}" : "";
        _log.Information("player {PeerId} joined ({InGame} in game){OnTeam:l}", player.PeerId,
            _runtime.World.Players.Count, onTeam);
    }
}
