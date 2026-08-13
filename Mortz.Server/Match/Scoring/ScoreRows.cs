using Mortz.Core.Match.Teams;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring;

/// <summary>A score row frozen at one moment, so a DeathScore keeps the tallies
/// as they were when the death was scored.</summary>
public readonly record struct PlayerScore(Team? Team, int Kills, int Deaths);

/// <summary>One seated player and their score row frozen at the same moment.</summary>
public readonly record struct SeatedScore(Player Player, PlayerScore Score);
