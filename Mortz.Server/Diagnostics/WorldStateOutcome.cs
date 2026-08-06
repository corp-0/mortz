namespace Mortz.Server.Diagnostics;

public readonly record struct WorldStateOutcome(int Tick, E2EWorldPlayer[] Players);
