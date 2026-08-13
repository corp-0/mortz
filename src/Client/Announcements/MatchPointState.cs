using Mortz.Core.Match.Teams;

namespace Mortz.Client.Announcements;

public readonly record struct MatchPointLeader(string Name, Team? Team);

/// <summary>Leader is null while nobody has a meaningful lead.</summary>
public readonly record struct MatchPointState(int Remaining, MatchPointLeader? Leader);
