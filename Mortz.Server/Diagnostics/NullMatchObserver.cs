using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Diagnostics;

public sealed class NullMatchObserver : IMatchObserver
{
    public void PlayerJoined(Player player, ServerPhaseKind phase) { }

    public void PlayerLeft(Player player, ServerPhaseKind phase) { }

    public void PhaseChanged(ServerPhaseKind kind) { }

    public void MatchAdvanced(in MatchFrame frame) { }
}
