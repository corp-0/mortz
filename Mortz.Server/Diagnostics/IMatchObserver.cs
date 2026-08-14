using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Diagnostics;

/// <summary>Read-only tap on the server's lifecycle for a process driver
/// reporting what happened; nothing here may change server state.</summary>
public interface IMatchObserver
{
    void PlayerJoined(Player player, ServerPhaseKind phase);
    void PlayerLeft(Player player, ServerPhaseKind phase);
    void PhaseChanged(ServerPhaseKind kind);
    void MatchAdvanced(MatchUpdate update);
}
