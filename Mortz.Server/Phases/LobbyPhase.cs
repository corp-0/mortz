using Mortz.Server.Admin;
using Mortz.Server.Chat;
using Mortz.Server.Lobby;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Serilog;

namespace Mortz.Server.Phases;

/// <summary>Everything whose lifetime is exactly one lobby.</summary>
public sealed class LobbyPhase : ServerPhase
{
    private readonly LobbySession _session;
    private readonly LobbyStateKeys _keys;
    private readonly LobbyService _lobby;
    private readonly Roster _roster;
    private readonly ILogger _log;
    private readonly PhaseControl _control;
    private readonly object[] _services;

    private LobbyPhase(LobbySession session, LobbyStateKeys keys, LobbyService lobby,
        LobbySettingsService lobbySettings, Roster roster, ILogger log, PhaseControl control)
    {
        _session = session;
        _keys = keys;
        _lobby = lobby;
        _roster = roster;
        _log = log;
        _control = control;
        _services = [_lobby, lobbySettings];
    }

    public override ServerPhaseKind Kind => ServerPhaseKind.LOBBY;

    public override IReadOnlyList<object> Services => _services;

    public bool CanStart => _session.CanStart;

    public SeatAssignment[] Seats => [
        .._session.Members.Select(member => new SeatAssignment(member, _session.TeamOf(member)))
    ];

    public static LobbyPhase Open(Roster roster, SettingsService settings, AdminService admin,
        ChatService chat, IServerLink link, ILogger log, PhaseControl control, int generation)
    {
        LobbyStateKeys keys = new(generation);
        LobbySession session = new(keys, settings.Config.Rules.Teams);
        LobbyService lobby = new(session, link, log, control);
        LobbySettingsService lobbySettings = new(settings, admin, chat, lobby, log);
        return new LobbyPhase(session, keys, lobby, lobbySettings, roster, log, control);
    }

    public override void OpenPhaseKeys(Player player) =>
        player.OpenLobby(_keys.Count, _keys.Generation);

    /// <summary>Seating writes lobby state, so it must wait until every roster
    /// player has it open.</summary>
    public override void Begin()
    {
        foreach (Player player in _roster)
        {
            _session.Add(player);
        }
        _lobby.BroadcastRoster();
    }

    public override void PlayerJoined(Player player)
    {
        _session.Add(player);
        _log.Information("player {PeerId} '{PlayerName}' entered lobby ({Waiting} waiting)",
            player.PeerId, player.Name, _session.Count);
        _lobby.BroadcastRoster();
    }

    public override void PlayerLeft(Player player)
    {
        if (!_session.Remove(player))
            return;
        _log.Information("player {PeerId} left lobby ({Waiting} waiting)", player.PeerId,
            _session.Count);
        _lobby.BroadcastRoster();
        if (_session.CanStart)
            _control.Request(PhaseRequest.START_MATCH);
    }

    /// <summary>Lobby transitions arrive through PhaseControl, never from here.</summary>
    public override PhaseRequest Advance(ServerTime time) => PhaseRequest.NONE;
}
