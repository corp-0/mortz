namespace Mortz.E2E.Tests.Harness;

/// <summary>A position in one process's event history. The default value is
/// before every event ever published.</summary>
public readonly record struct E2EEventCursor(long Sequence);
