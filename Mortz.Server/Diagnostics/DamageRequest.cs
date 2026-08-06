namespace Mortz.Server.Diagnostics;

/// <summary>Damage waiting for a tick boundary. Error is set when the tick
/// arrives and the target turns out to be gone.</summary>
public readonly record struct DamageRequest(
    int PeerId,
    int Amount,
    Action<DamageOutcome> Done,
    string? Error = null);
