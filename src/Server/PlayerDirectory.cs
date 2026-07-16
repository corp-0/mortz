using Mortz.Core.Net;

namespace Mortz.Server;

/// <summary>Process-lifetime identity state. Lobby and match sessions can be
/// replaced without losing the names of connected players.</summary>
internal sealed class PlayerDirectory
{
    private readonly SortedDictionary<long, string> _names = new();

    public int Count => _names.Count;
    public IEnumerable<long> PeerIds => _names.Keys;

    public bool Contains(long peerId) => _names.ContainsKey(peerId);

    public string Add(long peerId, string requestedName)
    {
        string name = PlayerNameSanitizer.Sanitize(requestedName);
        if (name.Length == 0)
            name = $"Player {peerId}";
        _names[peerId] = name;
        return name;
    }

    public void Remove(long peerId) => _names.Remove(peerId);

    public string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : peerId.ToString();
}
