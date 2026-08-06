namespace Mortz.E2E.Protocol;

/// <summary>Liveness pulse, once per sim second.</summary>
public sealed record MatchTickEvent(int Tick) : E2EEvent;
