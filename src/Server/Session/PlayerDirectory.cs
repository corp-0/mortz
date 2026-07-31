using Mortz.Core.Net;

namespace Mortz.Server.Session;

/// <summary>Process-lifetime identity state. Lobby and match sessions can be
/// replaced without losing the names of connected players.</summary>
internal sealed class PlayerDirectory
{
    private readonly SortedDictionary<int, string> _names = new();

    public int Count => _names.Count;
    public IEnumerable<int> PeerIds => _names.Keys;
    public IReadOnlyDictionary<int, string> Named => _names;

    public bool Contains(int peerId) => _names.ContainsKey(peerId);

    public string Add(int peerId, string requestedName)
    {
        string name = ForRequested(requestedName, peerId);
        _names[peerId] = name;
        return name;
    }

    public void Remove(int peerId) => _names.Remove(peerId);

    public string Name(int peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"<unknown {peerId}>";

    private static string ForRequested(string? requested, int peerId)
    {
        string sanitized = PlayerNameSanitizer.Sanitize(requested);
        return sanitized.Length > 0 ? sanitized : $"Player {peerId}";
    }
}
