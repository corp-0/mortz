using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring;

/// <summary>One seated player and their score row frozen at the same moment.</summary>
public readonly record struct SeatedScore(Player Player, PlayerScore Score);
