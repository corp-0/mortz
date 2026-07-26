namespace Mortz.Client.Announcements;

/// <summary>A player as the roster knew them when the event landed. Team is 0
/// when teams are off.</summary>
public readonly record struct Combatant(long Id, string Name, byte Team);
