namespace Mortz.Server.Diagnostics;

public readonly record struct MutationOutcome(int AppliedTick, string? Error);
