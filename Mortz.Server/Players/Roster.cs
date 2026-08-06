using System.Collections;
using Mortz.Core.Net;
using Mortz.Core.Net.Names;
using Mortz.Core.Sim;

namespace Mortz.Server.Players;

/// <summary>Everyone currently admitted, in ascending peer id. The one place a
/// identity is decided, so whoever holds a Player holds its name and skin.</summary>
public sealed class Roster(ServerStateKeys keys) : IReadOnlyCollection<Player>
{
    private readonly SortedDictionary<int, Player> _players = [];

    public int Count => _players.Count;

    public Player? Find(int peerId) =>
        _players.GetValueOrDefault(peerId);

    public IEnumerator<Player> GetEnumerator() => _players.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public Player Join(int peerId, string requestedName, int requestedSkin = 0)
    {
        if (requestedSkin is < 0 or >= SimConfig.SKIN_COUNT)
            throw new ArgumentOutOfRangeException(nameof(requestedSkin));
        string sanitized = PlayerNameSanitizer.Sanitize(requestedName);
        string name = sanitized.Length > 0 ? sanitized : $"Player {peerId}";
        Player player = new(peerId, name, keys.Count, keys.Generation, (byte)requestedSkin);
        _players[peerId] = player;
        return player;
    }

    public Player? Leave(int peerId)
    {
        return !_players.Remove(peerId, out Player? player) ? null : player;
    }
}
