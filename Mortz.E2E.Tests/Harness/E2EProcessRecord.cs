namespace Mortz.E2E.Tests.Harness;

public sealed record E2EProcessRecord(
    string Name,
    int ProcessId,
    string CommandLine,
    int? ExitCode,
    long CorruptLines,
    long DroppedEvents);
