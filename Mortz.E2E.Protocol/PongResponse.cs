namespace Mortz.E2E.Protocol;

public sealed record PongResponse(Guid Id, string SchemaHash) : E2EResponse(Id);
