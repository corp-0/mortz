using Mortz.Core.Identity;
using Mortz.Server.Players;

namespace Mortz.Server.Platform;

public interface IServerPlayerReporting
{
    ulong CreateGuest();
    bool Update(ulong accountId, string name);
    void EndGuest(ulong accountId);
}

public class ServerPlayerReporting(IServerPlayerReporting backend) : IDisposable
{
    private readonly Dictionary<Player, ulong> _guests = new();
    private bool _disposed;

    public void Update(IEnumerable<Player> players, bool available)
    {
        if (_disposed) { return; }
        HashSet<Player> current = available ? new(players) : [];
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
                if (guestId == 0) { continue; }
                _guests.Add(player, guestId);
            }
            if (!backend.Update(guestId, player.Name))
            {
                _guests.Remove(player);
                backend.EndGuest(guestId);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        Update([], false);
        _disposed = true;
    }
}
