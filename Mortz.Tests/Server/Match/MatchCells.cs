using Mortz.Server.Players;

namespace Mortz.Tests.Server.Match;

/// <summary>Mints players with open match cells for one match lifetime. Join
/// players only after every service under test has claimed its keys.</summary>
public sealed class MatchCells
{
    private const int GENERATION = 1;

    private readonly SortedDictionary<int, Player> _seated = [];

    public MatchStateKeys Keys { get; } = new(GENERATION);

    public IReadOnlyDictionary<int, Player> Seated => _seated;

    public Player GetOrJoin(int peerId)
    {
        if (_seated.TryGetValue(peerId, out Player? present))
            return present;
        Player player = new(peerId, $"Player {peerId}", serverKeyCount: 0,
            serverGeneration: GENERATION);
        player.OpenMatch(Keys.Count, Keys.Generation);
        _seated[peerId] = player;
        return player;
    }

    public void Leave(Player player) => _seated.Remove(player.PeerId);
}
