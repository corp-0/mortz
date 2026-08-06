using Mortz.E2E.Protocol;

namespace Mortz.E2E.Tests.Harness;

/// <summary>The typed client surface. The send helper is constrained to
/// IClientRequest, so handing it a server request is a compile error.</summary>
public sealed class ClientDriver(E2EProcess process, E2EBudget budget, string name, int peerId)
{
    public E2EProcess Process => process;

    public E2EEventStream Events => process.Events;

    public string Name => name;

    public int PeerId => peerId;

    public Task<PongResponse> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync<PingRequest, PongResponse>(
            new PingRequest(Guid.NewGuid(), E2EProtocolSchema.Hash), cancellationToken);

    public Task<ReadySentResponse> SetReadyAsync(
        bool ready, CancellationToken cancellationToken = default) =>
        SendAsync<SetReadyRequest, ReadySentResponse>(
            new SetReadyRequest(Guid.NewGuid(), ready), cancellationToken);

    public Task<InputPlanAcceptedResponse> RunPlanAsync(
        BotInputPlan plan, CancellationToken cancellationToken = default) =>
        SendAsync<RunInputPlanRequest, InputPlanAcceptedResponse>(
            new RunInputPlanRequest(Guid.NewGuid(), plan), cancellationToken);

    public Task<ClientStateResponse> StateAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ClientStateRequest, ClientStateResponse>(
            new ClientStateRequest(Guid.NewGuid()), cancellationToken);

    public Task<ShutdownStartedResponse> ShutdownAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<ShutdownRequest, ShutdownStartedResponse>(
            new ShutdownRequest(Guid.NewGuid()), cancellationToken);

    private Task<TResponse> SendAsync<TRequest, TResponse>(
        TRequest request, CancellationToken cancellationToken)
        where TRequest : E2ERequest, IE2ERequest<TResponse>, IClientRequest
        where TResponse : E2EResponse =>
        process.SendAsync<TRequest, TResponse>(request, budget.Response, cancellationToken);
}
