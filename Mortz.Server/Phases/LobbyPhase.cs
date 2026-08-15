using Mortz.Core.Match.Teams;
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
    private readonly LobbyReplication _replication;
    private readonly Roster _roster;
    private readonly ILogger _log;
    private readonly IPhaseTransitionRequests _transitions;
    private readonly object[] _services;

    private LobbyPhase(Roster roster, SettingsService settings, AdminService admin,
        ChatService chat, IServerLink link, ILogger log, IPhaseTransitionRequests transitions)
    {
        _session = new LobbySession(settings.Config.Rules.Teams);
        _replication = new LobbyReplication(link);
        _roster = roster;
        _log = log;
        _transitions = transitions;

        LobbyService lobby = new(_session, Apply);
        LobbySettingsService lobbySettings = new(
            settings, admin, chat, _session, Apply, log);
        _services = [lobby, lobbySettings];
    }

    public override ServerPhaseKind Kind => ServerPhaseKind.LOBBY;

    public override IReadOnlyList<object> Services => _services;

    public bool CanStart => _session.CanStart;

    public SeatAssignment[] Seats => _session.Seats;

    public static LobbyPhase Open(Roster roster, SettingsService settings, AdminService admin,
        ChatService chat, IServerLink link, ILogger log, IPhaseTransitionRequests transitions) =>
        new(roster, settings, admin, chat, link, log, transitions);

    public override void Begin() => Apply(_session.Initialize(_roster));

    public override void PlayerJoined(Player player) => Apply(_session.Join(player));

    public override void PlayerLeft(Player player) => Apply(_session.Leave(player));

    /// <summary>Lobby transitions arrive through the coordinator, never from here.</summary>
    public override PhaseRequest Advance(ServerTime time) => PhaseRequest.NONE;

    private void Apply(LobbyUpdate? update)
    {
        if (update == null)
            return;

        Log(update.Change);
        _replication.Publish(update);
        if (update.CanStart)
            _transitions.RequestStartMatch();
    }

    private void Log(LobbyChange change)
    {
        switch (change)
        {
            case LobbyChange.Initialized:
                break;
            case LobbyChange.Joined joined:
                _log.Information("player {PeerId} '{PlayerName}' entered lobby ({Waiting} waiting)",
                    joined.PeerId, joined.Name, _session.Count);
                break;
            case LobbyChange.Left left:
                _log.Information("player {PeerId} left lobby ({Waiting} waiting)", left.PeerId,
                    _session.Count);
                break;
            case LobbyChange.ReadinessChanged readiness:
                _log.Information("player {PeerId} is {Readiness}", readiness.PeerId,
                    readiness.Ready ? "ready" : "not ready");
                break;
            case LobbyChange.TeamChanged team:
                _log.Information("player {PeerId} moved to {Team}", team.PeerId,
                    Teams.Name(team.Team));
                break;
            case LobbyChange.SwapOffered offer:
                _log.Information("player {PeerId} offers a team swap to {TargetPeerId}",
                    offer.PeerId, offer.TargetPeerId);
                break;
            case LobbyChange.SwapCancelled cancellation:
                _log.Information("player {PeerId} cancelled their swap offer",
                    cancellation.PeerId);
                break;
            case LobbyChange.TeamsSwapped swap:
                _log.Information("players {PeerId} and {TargetPeerId} swapped teams",
                    swap.PeerId, swap.TargetPeerId);
                break;
            case LobbyChange.TeamsRuleChanged teams:
                _log.Information("lobby teams {TeamsState}", teams.Enabled ? "assigned" : "cleared");
                break;
        }
    }
}
