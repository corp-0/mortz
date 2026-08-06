using Mortz.Core.Match;
using Mortz.Core.Match.Events;

namespace Mortz.Client.Announcements;

/// <summary>One judged fact with names already resolved.</summary>
public readonly record struct Announcement(
    GameEventKind Kind,
    Combatant Actor,
    Combatant Victim,
    byte Magnitude,
    SuicideCause Cause = SuicideCause.BLAST
);
