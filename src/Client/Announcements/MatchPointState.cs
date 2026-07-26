namespace Mortz.Client.Announcements;

/// <summary>Leader is a display name ("p1", "Team 2"), null while nobody
/// leads (a kill target of 1 holds match point with an empty board).</summary>
public readonly record struct MatchPointState(bool Active, byte Remaining, string? Leader);
