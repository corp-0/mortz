using Mortz.Core.Match.Events;
using Mortz.Core.Match.Teams;

namespace Mortz.Client.Announcements;

/// <summary>A player as the roster knew them when the event landed. Team is
/// null when teams are off.</summary>
public readonly record struct Combatant(int Id, string Name, Team? Team);

/// <summary>One judged fact with names already resolved.</summary>
public readonly record struct Announcement(
    GameEventKind Kind,
    Combatant Actor,
    Combatant Victim,
    byte Magnitude,
    SuicideCause Cause = SuicideCause.BLAST
);
