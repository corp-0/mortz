namespace Mortz.Core.Sim;

public readonly record struct SpawnPointValidationError(int Index, Vec2 Position, string Reason);
