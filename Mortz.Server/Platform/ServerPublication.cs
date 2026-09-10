using Mortz.Core.Identity;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Players;

namespace Mortz.Server.Platform;

public interface IServerPublication
{
    void Publish(ServerInfo info);
    void Advertise(bool active);
    ulong CreateGuest();
    bool Update(ulong accountId, string name);
    void EndGuest(ulong accountId);
}

public class ServerPublication(IServerPublication backend) : IDisposable
{
    public const ulong UPDATE_INTERVAL_MS = 1_000;
    private readonly Dictionary<Player, ulong> _guests = [];
    private ServerInfo? _published;
    private ulong _nextUpdate;
    private bool _disposed;
    public bool Active { get; private set; }

    public void Advance(ServerInfo info, IEnumerable<Player> players, bool available, bool isPublic, ulong now)
    {
        if (_disposed)
            return;
        if (!available)
            UpdatePlayers([]);
        if (now >= _nextUpdate)
        {
            UpdatePlayers(available ? players : []);
            if (_published != info)
            {
                backend.Publish(info);
                _published = info;
            }
            _nextUpdate = now + UPDATE_INTERVAL_MS;
        }
        bool active = available && isPublic;
        if (active != Active)
        {
            backend.Advertise(active);
            Active = active;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        backend.Advertise(false);
        Active = false;
        UpdatePlayers([]);
        GC.SuppressFinalize(this);
    }

    private void UpdatePlayers(IEnumerable<Player> players)
    {
        HashSet<Player> current = [.. players];
        foreach ((Player player, ulong accountId) in _guests.ToArray())
        {
            if (!current.Contains(player))
            {
                _guests.Remove(player);
                backend.EndGuest(accountId);
            }
        }
        foreach (Player player in current)
        {
            if (player.Account is { Provider: AccountProvider.STEAM } account)
            {
                // Verification owns this session; publication only supplies its visible name.
                backend.Update(account.AccountId, player.Name);
                continue;
            }
            if (!_guests.TryGetValue(player, out ulong guestId))
            {
                guestId = backend.CreateGuest();
                if (guestId == 0)
                    continue;
                _guests.Add(player, guestId);
            }
            if (!backend.Update(guestId, player.Name))
            {
                _guests.Remove(player);
                backend.EndGuest(guestId);
            }
        }
    }
}
