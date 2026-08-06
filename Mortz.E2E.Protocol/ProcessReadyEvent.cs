namespace Mortz.E2E.Protocol;

public sealed record ProcessReadyEvent(
    E2EProcessRole Role,
    int ProcessId,
    string SchemaHash) : E2EEvent;
