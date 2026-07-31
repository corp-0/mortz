using Mortz.Core.Match;

namespace Mortz.Client.Announcements;

/// <summary>A player as the roster knew them when the event landed. Team is
/// null when teams are off.</summary>
public readonly record struct Combatant(int Id, string Name, Team? Team);
