namespace Mortz.Core.Sim;

/// <summary>Damage asked for between two Steps, applied inside the next one.</summary>
public readonly record struct PendingDamage(int PeerId, int Amount);
