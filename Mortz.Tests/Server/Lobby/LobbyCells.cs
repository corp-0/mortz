using Mortz.Server.Players;

namespace Mortz.Tests.Server.Lobby;

/// <summary>Mints players with open lobby cells for one lobby lifetime. Join
/// players only after every service under test has claimed its keys.</summary>
public sealed class LobbyCells
{
    private const int GENERATION = 1;

    private readonly SortedDictionary<int, Player> _seated = [];

    public LobbyStateKeys Keys { get; } = new(GENERATION);

    public Player GetOrJoin(int peerId)
    {
        if (_seated.TryGetValue(peerId, out Player? present))
            return present;
        Player player = new(peerId, $"Player {peerId}", serverKeyCount: 0,
            serverGeneration: GENERATION);
        player.OpenLobby(Keys.Count, Keys.Generation);
        _seated[peerId] = player;
        return player;
    }
}
