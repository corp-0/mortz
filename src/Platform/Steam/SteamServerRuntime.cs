#if MORTZ_STEAM
using Godot;
using System.Globalization;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Platform;
using Mortz.Server.Admission;
using Mortz.Server.Players;
using Mortz.Shared.Logging;

namespace Mortz.Platform.Steam;

public class SteamServerRuntime : IServerPlatform, IServerPacketRouter, IVerificationBackend
{
    private GodotSteamServerApi? _api;
    private bool _initialized;
    private bool _disposed;
    private bool _isPublic;
    private readonly Func<IEnumerable<Player>> _players;
    private ServerPublication? _publication;
    public VerificationSessions Verification { get; }

    public SteamServerRuntime(Func<IEnumerable<Player>> players)
    {
        _players = players;
        Verification = new VerificationSessions(this);
    }

    bool IVerificationBackend.Available => AuthenticationAvailable;
    ulong IVerificationBackend.ServerAccountId => AuthenticationAvailable ? _api!.AccountId : 0;
    bool IVerificationBackend.Begin(byte[] ticket, ulong accountId) => _api!.BeginAuthSession(ticket, accountId) == 0;
    void IVerificationBackend.End(ulong accountId) => _api?.EndAuthSession(accountId);
    private void Validated(ulong accountId, long result, ulong ownerId) => Verification.Result(accountId, result == 0);

    public bool AuthenticationAvailable => _initialized && !_disposed && _api!.LoggedOn() && _api.AccountId != 0;
    public bool PublicationActive => AuthenticationAvailable && _publication!.Active;
    public string Status { get; private set; } = "Steam unavailable; guest play is ready.";

    public bool Initialize(ServerInfo info, int queryPort, bool isPublic)
    {
        if (_api != null || _disposed)
        {
            throw new InvalidOperationException("Steam server runtime already started or disposed.");
        }
        if (isPublic && queryPort == ushort.MaxValue)
        {
            Status = "Steam reserves query port 65535; using standalone queries and guest play.";
            return false;
        }
        try
        {
            OS.SetEnvironment("SteamAppId", info.AppId.ToString(CultureInfo.InvariantCulture));
            _api = new GodotSteamServerApi();
            _api.Connected += Connected;
            _api.ConnectFailure += ConnectFailure;
            _api.Disconnected += Disconnected;
            _api.AuthTicketValidated += Validated;
            using Godot.Collections.Dictionary result = _api.Initialize("0.0.0.0", checked((ushort)info.GamePort),
                isPublic ? checked((ushort)queryPort) : ushort.MaxValue, info.Version);
            _initialized = result.TryGetValue("status", out Variant status) && status.AsInt32() == 0;
            if (!_initialized)
            {
                Status = "Steam initialization failed; guest play is ready.";
                return false;
            }
            _isPublic = isPublic;
            _publication = new ServerPublication(_api);
            _api.Advertise(false);
            _api.SetProduct(info.AppId.ToString());
            _api.SetGameDescription("Mortz");
            _api.SetModDir("mortz");
            _api.SetDedicatedServer(true);
            _publication.Advance(info, [], false, isPublic, Time.GetTicksMsec());
            _api.LogOnAnonymous();
            Status = "Steam login pending; guest play is ready.";
            return true;
        }
        catch (Exception exception)
        {
            Status = "Steam initialization failed; guest play is ready.";
            MortzLog.For("steam").Warning("Steam server initialization failed: {Reason}", exception.Message);
            return false;
        }
    }

    public void Advance(ServerInfo info)
    {
        if (!_initialized || _disposed)
        {
            return;
        }
        Verification.Pump(_api!.RunCallbacks);
        _publication!.Advance(info, _players(), AuthenticationAvailable, _isPublic, Time.GetTicksMsec());
    }

    public bool HandleIncoming(byte[] packet, string address, int port) =>
        _initialized && !_disposed && !_isPublic && _api!.HandleIncomingPacket(packet, address, port);

    public ServerDatagram? TakeOutgoing() =>
        _initialized && !_disposed && !_isPublic ? _api!.TakeOutgoingPacket() : null;

    private void Connected() => Status = "Steam connected.";
    private void ConnectFailure(long result, bool retrying) =>
        Status = $"Steam login unavailable ({result}); guest play is ready.";
    private void Disconnected(long result) =>
        Status = $"Steam disconnected ({result}); guest play is ready.";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _publication?.Dispose();
        Verification.Dispose();
        if (_api != null)
        {
            if (_initialized)
            {
                _api.LogOff();
            }
            _api.Shutdown();
            _api.Dispose();
            _api = null;
        }
        _initialized = false;
    }
}
#endif
