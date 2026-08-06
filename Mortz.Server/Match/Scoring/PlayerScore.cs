using Mortz.Core.Match;
using Mortz.Core.Match.Teams;

namespace Mortz.Server.Match.Scoring;

/// <summary>A score row frozen at one moment, so a DeathScore keeps the tallies
/// as they were when the death was scored.</summary>
public readonly record struct PlayerScore(Team? Team, int Kills, int Deaths);
