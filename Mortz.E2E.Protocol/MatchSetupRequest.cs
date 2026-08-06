namespace Mortz.E2E.Protocol;

/// <summary>Asks the server what it is actually simulating, so a scenario never
/// has to assume the shipped constants.</summary>
public sealed record MatchSetupRequest(Guid Id)
    : E2ERequest(Id), IE2ERequest<MatchSetupResponse>, IServerRequest;
