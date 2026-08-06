using Mortz.Core.Sim;
using Mortz.E2E.Protocol;

namespace Mortz.E2E.Tests.Harness;

/// <summary>The typed server surface. The send helper is constrained to
/// IServerRequest, so handing it a client request is a compile error.</summary>
public sealed class ServerDriver(E2EProcess process, E2EBudget budget)
{
    public E2EProcess Process => process;

    public E2EEventStream Events => process.Events;

    public Task<PongResponse> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync<PingRequest, PongResponse>(
            new PingRequest(Guid.NewGuid(), E2EProtocolSchema.Hash), cancellationToken);

    public Task<ServerStateResponse> StateAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ServerStateRequest, ServerStateResponse>(
            new ServerStateRequest(Guid.NewGuid()), cancellationToken);

    /// <summary>What the live match is simulating: rules and arena, so a
    /// scenario never assumes the shipped constants.</summary>
    public Task<MatchSetupResponse> SetupAsync(CancellationToken cancellationToken = default) =>
        SendAsync<MatchSetupRequest, MatchSetupResponse>(
            new MatchSetupRequest(Guid.NewGuid()), cancellationToken);

    public Task<PlayerPlacedResponse> PlacePlayerAsync(
        int peerId, Vec2 position, CancellationToken cancellationToken = default) =>
        SendAsync<PlacePlayerRequest, PlayerPlacedResponse>(
            new PlacePlayerRequest(Guid.NewGuid(), peerId, position), cancellationToken);

    public Task<PlayerDamagedResponse> DamagePlayerAsync(
        int peerId, int amount, CancellationToken cancellationToken = default) =>
        SendAsync<DamagePlayerRequest, PlayerDamagedResponse>(
            new DamagePlayerRequest(Guid.NewGuid(), peerId, amount), cancellationToken);

    public Task<ShutdownStartedResponse> ShutdownAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<ShutdownRequest, ShutdownStartedResponse>(
            new ShutdownRequest(Guid.NewGuid()), cancellationToken);

    private Task<TResponse> SendAsync<TRequest, TResponse>(
        TRequest request, CancellationToken cancellationToken)
        where TRequest : E2ERequest, IE2ERequest<TResponse>, IServerRequest
        where TResponse : E2EResponse =>
        process.SendAsync<TRequest, TResponse>(request, budget.Response, cancellationToken);
}
