using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Server.Players;

namespace Mortz.Server.Phases;

/// <summary>The lobby-to-match handoff for one player: the Player and the team
/// they froze on. A plain snapshot because the lobby cells are already closed
/// when the match phase seats everyone.</summary>
public readonly record struct SeatAssignment(Player Player, Team? Team);
