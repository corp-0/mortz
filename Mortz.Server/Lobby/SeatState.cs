using Mortz.Core.Match;
using Mortz.Core.Match.Teams;

namespace Mortz.Server.Lobby;

/// <summary>Lobby-lifetime cell: one player's ready flag and team seat. Team is
/// null while teams are off or the seat is not dealt yet.</summary>
public sealed class SeatState
{
    public bool Ready;
    public Team? Team;
}
