using Mortz.Protocol.Net.Query;
using Mortz.Server.Players;

namespace Mortz.Server.Platform;

public interface IServerPublication : IServerPlayerReporting
{
    void Publish(ServerInfo info);
    void Advertise(bool active);
}

public class ServerPublication(IServerPublication backend) : IDisposable
{
    public const ulong UPDATE_INTERVAL_MS = 1_000;
    private readonly ServerPlayerReporting _players = new(backend);
    private ServerInfo? _published;
    private ulong _nextUpdate;
    private bool _disposed;
    public bool Active { get; private set; }

    public void Advance(ServerInfo info, IEnumerable<Player> players, bool available, bool isPublic, ulong now)
    {
        if (_disposed) { return; }
        if (!available) { _players.Update([], false); }
        if (now >= _nextUpdate)
        {
            _players.Update(players, available);
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
        if (_disposed) { return; }
        _disposed = true;
        backend.Advertise(false);
        Active = false;
        _players.Dispose();
    }
}
