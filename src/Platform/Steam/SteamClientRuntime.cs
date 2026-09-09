#if MORTZ_STEAM
using Godot;
using Mortz.Client.Session;
using Mortz.Client.Servers;
using Mortz.Shared.Logging;
using SteamApi = GDExtension.Wrappers.Steam;

namespace Mortz.Platform.Steam;

public class SteamClientRuntime : IDisposable
{
    private SteamApi? _api;
    private bool _initialized;
    private bool _disposed;
    private SteamClientTickets? _tickets;
    private SteamInternetDiscovery? _discovery;
    public IInternetDiscovery Discovery => _discovery ?? (IInternetDiscovery)new UnavailableInternetDiscovery();
    public IClientTicketProvider Tickets => _tickets ?? (IClientTicketProvider)new GuestTicketProvider();
    public string? PersonaName => Available ? _api!.GetPersonaName() : null;
    public bool Available => _initialized && _api!.LoggedOn();
    public string Status { get; private set; } = "Steam unavailable; guest play is ready.";

    public void Start()
    {
        if (_api != null || _disposed)
        {
            throw new InvalidOperationException("Steam client runtime already started or disposed.");
        }
        if (!Engine.HasSingleton("Steam"))
        {
            return;
        }
        try
        {
            _api = SteamApi.Bind(Engine.GetSingleton("Steam"));
            // Distributed clients get their App ID from Steam's launch environment.
            _initialized = _api.SteamInit(0, false);
            if (_initialized)
            {
                _discovery = new SteamInternetDiscovery(_api, () => Available);
                _tickets = new SteamClientTickets(_api, () => Available);
                _api.GetAuthSessionTicketResponseSignal += _tickets.Ready;
            }
            Status = _initialized ? "Steam ready." : "Steam unavailable; guest play is ready.";
        }
        catch (Exception exception)
        {
            _initialized = false;
            _discovery?.Dispose();
            _discovery = null;
            ReleaseTickets();
            Status = "Steam initialization failed; guest play is ready.";
            MortzLog.For("steam").Warning("Steam client initialization failed: {Reason}", exception.Message);
        }
        if (!_initialized)
        {
            _api?.SteamShutdown();
        }
        MortzLog.For("steam").Information("{Status}", Status);
    }

    public void Advance()
    {
        if (_initialized && !_disposed)
        {
            _api!.RunCallbacks();
            _discovery?.Advance();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _discovery?.Dispose();
        _discovery = null;
        ReleaseTickets();
        if (_initialized)
        {
            _initialized = false;
            _api!.SteamShutdown();
        }
        // The addon owns its singleton and attached wrapper.
        _api = null;
    }

    private void ReleaseTickets()
    {
        SteamClientTickets? tickets = _tickets;
        _tickets = null;
        if (tickets == null)
        {
            return;
        }
        _api!.GetAuthSessionTicketResponseSignal -= tickets.Ready;
        tickets.Dispose();
    }
}
#endif
