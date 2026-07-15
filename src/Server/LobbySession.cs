namespace Mortz.Server;

internal readonly record struct LobbyPlayer(long PeerId, bool Ready);

/// <summary>Ready-up state for the current lobby. It owns no match resources,
/// so returning from a match is a fresh session rather than a partial reset.</summary>
internal sealed class LobbySession
{
    private readonly SortedDictionary<long, bool> _ready = new();

    public int Count => _ready.Count;
    public bool CanStart => _ready.Count > 0 && !_ready.ContainsValue(false);
    public IReadOnlyList<LobbyPlayer> Players =>
        _ready.Select(pair => new LobbyPlayer(pair.Key, pair.Value)).ToArray();

    public void Add(long peerId) => _ready[peerId] = false;
    public bool Remove(long peerId) => _ready.Remove(peerId);

    public bool SetReady(long peerId, bool ready)
    {
        if (!_ready.ContainsKey(peerId))
            return false;
        _ready[peerId] = ready;
        return true;
    }

    public static LobbySession For(IEnumerable<long> peerIds)
    {
        LobbySession lobby = new();
        foreach (long peerId in peerIds)
            lobby.Add(peerId);
        return lobby;
    }
}
