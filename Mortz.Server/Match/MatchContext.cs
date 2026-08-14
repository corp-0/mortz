using Mortz.Core.Sim;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>Shared match-lifetime data available to ordered match steps.</summary>
public class MatchContext(
    SimWorld world,
    IReadOnlyDictionary<int, Player> seatedPlayers)
{
    public SimWorld World { get; } = world;

    /// <summary>Players seated in the match, distinct from simulated players and
    /// JIP spectators.</summary>
    public IReadOnlyDictionary<int, Player> SeatedPlayers { get; } = seatedPlayers;

    public MatchStage Stage { get; internal set; } = MatchStage.PLAYING;
}
