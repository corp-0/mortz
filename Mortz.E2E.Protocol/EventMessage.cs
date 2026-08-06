namespace Mortz.E2E.Protocol;

public sealed record EventMessage(E2EEvent Event) : E2EMessage;
