using Mortz.Core.Net;
using Mortz.Core.Net.Query;

namespace Mortz.Client.Servers;

/// <summary>
/// The browser's model: the pinned playtest server, saved favorites, LAN
/// answers and direct-connect finds. There is no master server, so this list
/// is the whole world.
/// </summary>
public sealed class ServerList
{
    /// <summary>Cannot be unfavorited, or a fresh install has nowhere to play.</summary>
    public static readonly ServerEndpoint PINNED_ENDPOINT =
        new("gillesgillespie.duckdns.org", NetConfig.DEFAULT_PORT);

    private const string PINNED_LABEL = "Mortz Playtest";

    private readonly List<ServerEntry> _entries = [];

    public ServerList(IEnumerable<FavoriteServer>? favorites = null)
    {
        _entries.Add(new ServerEntry(PINNED_ENDPOINT, ServerSource.PINNED, PINNED_LABEL));
        foreach (FavoriteServer favorite in favorites ?? [])
        {
            ServerEndpoint endpoint = new(favorite.Address, favorite.Port);
            if (Find(endpoint) == null)
                _entries.Add(new ServerEntry(endpoint, ServerSource.FAVORITE, favorite.Label));
        }
    }

    /// <summary>Grouped by source, insertion order within a group.</summary>
    public IReadOnlyList<ServerEntry> Entries =>
        _entries.OrderBy(entry => entry.SortRank).ToList();

    public IEnumerable<FavoriteServer> Favorites =>
        _entries.Where(entry => entry.Source == ServerSource.FAVORITE)
            .Select(entry => new FavoriteServer(entry.Endpoint.Address, entry.Endpoint.Port,
                entry.Label));

    public ServerEntry? Find(ServerEndpoint endpoint) =>
        _entries.FirstOrDefault(entry =>
            entry.Endpoint.Port == endpoint.Port &&
            string.Equals(entry.Endpoint.Address, endpoint.Address,
                StringComparison.OrdinalIgnoreCase));

    public ServerEntry AddDirect(ServerEndpoint endpoint)
    {
        if (Find(endpoint) is ServerEntry existing)
            return existing;
        ServerEntry entry = new(endpoint, ServerSource.DIRECT);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>An address already listed keeps its source: a favorite on this
    /// subnet stays a favorite.</summary>
    public ServerEntry AddDiscovered(ServerEndpoint endpoint)
    {
        if (Find(endpoint) is ServerEntry existing)
            return existing;
        ServerEntry entry = new(endpoint, ServerSource.LAN);
        _entries.Add(entry);
        return entry;
    }

    public void MarkProbing()
    {
        foreach (ServerEntry entry in _entries)
        {
            entry.Status = ServerStatus.PROBING;
        }
    }

    public void ApplyReply(ServerEndpoint endpoint, ServerInfo info, int pingMs)
    {
        ServerEntry entry = AddDiscovered(endpoint);
        entry.Info = info;
        entry.PingMs = pingMs;
        entry.Status = IsCompatible(info) ? ServerStatus.ONLINE : ServerStatus.INCOMPATIBLE;
    }

    public void ApplyTimeout(ServerEndpoint endpoint)
    {
        if (Find(endpoint) is not ServerEntry entry)
            return;
        entry.Status = ServerStatus.OFFLINE;
        entry.PingMs = 0;
    }

    /// <summary>Unstarring demotes to a session entry rather than yanking the
    /// row out from under the selection; it just stops being saved.</summary>
    public bool ToggleFavorite(ServerEntry entry)
    {
        if (!entry.CanToggleFavorite)
            return false;
        entry.Source = entry.Source == ServerSource.FAVORITE
            ? ServerSource.DIRECT
            : ServerSource.FAVORITE;
        return true;
    }

    private static bool IsCompatible(ServerInfo info) =>
        info.ProtocolVersion == NetConfig.PROTOCOL_VERSION &&
        info.SchemaHash == NetRegistry.SCHEMA_HASH;
}
