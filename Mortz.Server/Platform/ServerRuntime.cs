using Mortz.Protocol.Net.Query;

namespace Mortz.Server.Platform;

/// <summary>Keeps query ownership independent of Steam login and gameplay readiness.</summary>
public class ServerRuntime(IServerPlatform? platform, Func<int, bool> startQuery, Action stopQuery) : IDisposable
{
    private bool _started;
    private bool _disposed;
    private bool _initialized;
    public ServerCapabilities Capabilities { get; private set; }

    public void Start(ServerInfo info, int queryPort, bool isPublic)
    {
        if (_started || _disposed)
        {
            throw new InvalidOperationException("Server runtime already started or disposed.");
        }
        _started = true;
        _initialized = platform?.Initialize(info, queryPort, isPublic) == true;
        if (!_initialized)
        {
            // The SDK may have acquired its query socket before reporting failure.
            platform?.Dispose();
        }
        QueryOwner owner = _initialized && isPublic
            ? QueryOwner.STEAM
            : startQuery(queryPort) ? QueryOwner.MORTZ : QueryOwner.NONE;
        if (owner == QueryOwner.NONE && _initialized)
        {
            platform!.Dispose();
            _initialized = false;
        }
        Capabilities = new ServerCapabilities(true, owner, false, false,
            owner == QueryOwner.NONE ? "Query port unavailable; direct joining is ready." : platform?.Status ?? "Standalone");
        RefreshCapabilities();
    }

    public void Advance(ServerInfo info)
    {
        if (!_initialized || _disposed)
        {
            return;
        }
        platform!.Advance(info);
        RefreshCapabilities();
    }

    private void RefreshCapabilities()
    {
        if (!_initialized)
        {
            return;
        }
        Capabilities = Capabilities with
        {
            Authentication = platform!.AuthenticationAvailable,
            Publication = platform.PublicationActive,
            Status = platform.Status,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_initialized)
        {
            platform!.Dispose();
        }
        stopQuery();
        Capabilities = default;
    }
}
