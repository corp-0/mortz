using Mortz.E2E.Protocol;

namespace Mortz.E2E.Tests.Harness;

public readonly record struct RecordedEvent(long Sequence, E2EEvent Event);
