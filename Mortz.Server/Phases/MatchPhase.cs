using Mortz.Core.Input;
using Mortz.Core.Match;
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

    private readonly MatchSession _session;
    private readonly MatchStateKeys _keys;
    private readonly MatchWire _wire;
    private readonly WinsFeature _wins;
    private readonly MapSnapshot _map;
    private readonly ILogger _log;
    private readonly IMatchObserver _observer;
    private readonly IMatchControl _control;
    private readonly bool _allowJoinInProgress;
    private readonly IReadOnlyList<SeatAssignment> _seats;
    private readonly object[] _features;

    private MatchPhase(MatchSession session, MatchStateKeys keys, MatchWire wire,
        WinsFeature wins, MapSnapshot map, ILogger log, IMatchObserver observer,
        IMatchControl control, bool allowJoinInProgress, IReadOnlyList<SeatAssignment> seats)
    {
        _session = session;
        _keys = keys;
        _wire = wire;
        _wins = wins;
        _map = map;
        _log = log;
        _observer = observer;
        _control = control;
        _allowJoinInProgress = allowJoinInProgress;
        _seats = seats;
        _features = [_wire];
    }

    public override ServerPhaseKind Kind => ServerPhaseKind.MATCH;

    public override IReadOnlyList<object> Features => _features;

    public static MatchPhase Open(IReadOnlyList<SeatAssignment> seats,
        SettingsFeature settings, WinsFeature wins, Roster roster, IServerLink link,
        ILogger log, bool netStats, int generation, IMatchObserver observer,
        IMatchControl control, bool allowJoinInProgress)
    {
        MapSnapshot map = settings.Map;
        int victoryLapTicks = (int)(MATCH_END_SECONDS * SimConfig.TICK_RATE);
        MatchStateKeys keys = new(generation);
        MatchSession session = new(map.BuildMask(), settings.Config, victoryLapTicks, keys,
            map.SpawnPoints, map.Zones);
        MatchWire wire = new(session, roster, map, link, log, netStats);
        return new MatchPhase(session, keys, wire, wins, map, log, observer, control,
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
        _wire.BroadcastRoster();
    }

    public override void PlayerJoined(Player player)
    {
        if (_allowJoinInProgress)
        {
            Seat(player, null);
        }
        else
        {
            _session.AddJipSpectator(player);
            _log.Information("player {PeerId} joined as spectator", player.PeerId);
        }
        _wire.BroadcastRoster();
    }

    public override void Load(Player player, int generation, bool initialPhase) =>
        _wire.Enter(player, generation, initialPhase);

    public override void PlayerLeft(Player player)
    {
        _session.RemovePlayer(player);
        _log.Information("player {PeerId} left ({InGame} in game)", player.PeerId,
            _session.World.Players.Count);
        _wire.BroadcastRoster();
    }

    public override void Inputs(Player player, byte[] packet)
    {
        if (!InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> inputs))
            return;
        _wire.RecordInputPayload(packet.Length);
        foreach ((int sequence, PlayerInput input) in inputs)
        {
            _session.EnqueueInput(player, sequence, input);
        }
    }

    public override PhaseRequest Advance(ServerTime time)
    {
        _control.ApplyBefore(_session.World);
        MatchFrame frame = _session.Step();
        _control.CompleteAfter(_session.World);
        _wire.Publish(frame, _session, time);
        if (frame.MatchEnded is Victor winner)
            _wins.Record(_session.Winners(winner));
        _observer.MatchAdvanced(frame);
        return frame.ReturnToLobby ? PhaseRequest.RETURN_TO_LOBBY : PhaseRequest.NONE;
    }

    private void Seat(Player player, Team? lobbyTeam)
    {
        if (_map.SpawnPoints.Length > 0 &&
            _session.World.Players.Count >= _map.SpawnPoints.Length)
        {
            _log.Warning(
                "map '{MapId}' only has {Spawns} spawn point(s) for {Players} players, " +
                "so some will share one",
                _map.MapId, _map.SpawnPoints.Length, _session.World.Players.Count + 1);
        }
        Team? team = _session.AddPlayer(player, lobbyTeam);
        string onTeam = team is Team assigned ? $" on {Teams.Name(assigned)}" : "";
        _log.Information("player {PeerId} joined ({InGame} in game){OnTeam:l}", player.PeerId,
            _session.World.Players.Count, onTeam);
    }
}
