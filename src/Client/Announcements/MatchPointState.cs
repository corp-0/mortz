namespace Mortz.Client.Announcements;

/// <summary>Leader is null while nobody has a meaningful lead.</summary>
public readonly record struct MatchPointState(int Remaining, MatchPointLeader? Leader);
