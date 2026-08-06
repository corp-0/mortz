namespace Mortz.Server.Diagnostics;

public readonly record struct DamageOutcome(
    int AppliedTick,
    int RemainingHealth,
    bool Died,
    string? Error);
