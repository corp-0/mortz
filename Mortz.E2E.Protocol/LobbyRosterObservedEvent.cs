namespace Mortz.E2E.Protocol;

public sealed record LobbyRosterObservedEvent(int PlayerCount) : E2EEvent;
