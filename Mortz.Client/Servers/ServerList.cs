using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

public class ServerList
{
    public static readonly ServerEndpoint PinnedEndpoint =
        new("gillesgillespie.duckdns.org", NetConfig.DEFAULT_PORT);
    private readonly List<ServerEntry> _entries = [];

    public ServerList(IEnumerable<FavoriteServer>? favorites = null)
    {
        _entries.Add(new ServerEntry(PinnedEndpoint, ServerSource.PINNED, "Mortz Playtest"));
        foreach (FavoriteServer favorite in favorites ?? [])
        {
            try
            {
                ServerEndpoint endpoint = favorite.QueryPort is int queryPort
                    ? new ServerEndpoint(favorite.Address, favorite.Port, queryPort)
                    : new ServerEndpoint(favorite.Address, favorite.Port);
                if (Find(endpoint) == null)
                {
                    _entries.Add(new ServerEntry(endpoint, ServerSource.FAVORITE, favorite.Label));
                }
            }
            catch (ArgumentException) { }
        }
    }

    public IReadOnlyList<ServerEntry> Entries => _entries.OrderBy(entry => entry.SortRank).ToList();
    public IEnumerable<FavoriteServer> Favorites =>
        _entries.Where(entry => entry.Source == ServerSource.FAVORITE)
            .Select(entry => new FavoriteServer(entry.Endpoint.Address, entry.Endpoint.Port,
                entry.Label, entry.Endpoint.QueryPort));

    public ServerEntry? Find(ServerEndpoint endpoint) => _entries.FirstOrDefault(entry =>
        entry.Endpoint.Port == endpoint.Port &&
        (entry.Endpoint.Address == endpoint.Address || entry.ResolvedAddresses.Contains(endpoint.Address)));

    public ServerEntry AddDirect(ServerEndpoint endpoint)
    {
        if (Find(endpoint) is ServerEntry existing) { return existing; }
        ServerEntry entry = new(endpoint, ServerSource.DIRECT);
        _entries.Add(entry);
        return entry;
    }

    public ServerEntry? AddDiscovered(ServerEndpoint endpoint, ServerSource source = ServerSource.LAN)
    {
        ServerEntry? entry = Find(endpoint);
        if (entry == null)
        {
            if (_entries.Count(item => !item.IsFavorite) >= ServerProbeCoordinator.MAX_TRANSIENT_RESULTS)
            {
                return null;
            }
            entry = new ServerEntry(endpoint, source);
            _entries.Add(entry);
        }
        entry.SeenOnLan |= source == ServerSource.LAN;
        entry.SeenOnSteam |= source == ServerSource.STEAM;
        return entry;
    }

    public ServerEntry? ApplyInternet(InternetServer server)
    {
        ServerEntry? entry = AddDiscovered(server.Endpoint, ServerSource.STEAM);
        if (entry != null)
        {
            entry.SteamAccountId = server.AccountId;
        }
        return entry;
    }

    public void MarkProbing()
    {
        // Refresh membership belongs to this scan, while saved and direct endpoints survive.
        _entries.RemoveAll(entry => entry is { IsFavorite: false, Source: ServerSource.LAN or ServerSource.STEAM });
        foreach (ServerEntry entry in _entries)
        {
            entry.SeenOnLan = false;
            entry.SeenOnSteam = false;
            entry.SteamAccountId = 0;
            entry.ResolvedAddresses.Clear();
            entry.Status = ServerStatus.PROBING;
        }
    }

    public void ConfirmResolved(ServerEndpoint endpoint, string? address)
    {
        if (address == null || Find(endpoint) is not ServerEntry entry) { return; }
        ServerEndpoint resolved = new(address, endpoint.Port, endpoint.QueryPort);
        ServerEntry? alias = Find(resolved);
        entry.ResolvedAddresses.Add(resolved.Address);
        if (alias == null || ReferenceEquals(alias, entry)) { return; }
        ServerEntry keep = alias.IsFavorite && !entry.IsFavorite ? alias : entry;
        ServerEntry remove = ReferenceEquals(keep, entry) ? alias : entry;
        // Independently saved hostnames remain independently editable favorites.
        if (keep.IsFavorite && remove.IsFavorite) return;
        keep.ResolvedAddresses.UnionWith(remove.ResolvedAddresses);
        keep.ResolvedAddresses.Add(remove.Endpoint.Address);
        keep.SeenOnLan |= remove.SeenOnLan;
        keep.SeenOnSteam |= remove.SeenOnSteam;
        if (keep.SteamAccountId == 0) { keep.SteamAccountId = remove.SteamAccountId; }
        if (keep.Info == null)
        {
            keep.Info = remove.Info;
            keep.PingMs = remove.PingMs;
            keep.Status = remove.Status;
        }
        _entries.Remove(remove);
    }

    public void ApplyReply(ServerEndpoint endpoint, ServerInfo info, int pingMs)
    {
        ServerEntry? entry = Find(endpoint) ?? AddDiscovered(endpoint);
        if (entry == null) return;
        entry.Info = info;
        entry.PingMs = pingMs;
        if (!info.MetadataValid)
        {
            entry.Status = ServerStatus.UNKNOWN;
        }
        else if (info.ProtocolVersion == NetConfig.PROTOCOL_VERSION && info.SchemaHash == NetRegistry.SCHEMA_HASH)
        {
            entry.Status = ServerStatus.ONLINE;
        }
        else
        {
            entry.Status = ServerStatus.INCOMPATIBLE;
        }
    }

    public void ApplyTimeout(ServerEndpoint endpoint)
    {
        if (Find(endpoint) is not ServerEntry entry || entry.Endpoint.QueryPort != endpoint.QueryPort) { return; }
        entry.Status = ServerStatus.OFFLINE;
        entry.PingMs = 0;
    }

    public bool ToggleFavorite(ServerEntry entry)
    {
        if (!entry.CanToggleFavorite) return false;
        if (entry.Source != ServerSource.FAVORITE)
            entry.Source = ServerSource.FAVORITE;
        else if (entry.SeenOnSteam)
            entry.Source = ServerSource.STEAM;
        else if (entry.SeenOnLan)
            entry.Source = ServerSource.LAN;
        else
            entry.Source = ServerSource.DIRECT;
        return true;
    }
}
