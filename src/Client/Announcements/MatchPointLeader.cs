using Mortz.Core.Match;

namespace Mortz.Client.Announcements;

/// <summary>The name to show and the team whose color it wears: a player's own
/// team, or the leading team itself.</summary>
public readonly record struct MatchPointLeader(string Name, Team? Team);
