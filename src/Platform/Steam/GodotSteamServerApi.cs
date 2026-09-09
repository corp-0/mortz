using System;
using System.Collections.Generic;
using Godot;
using System.Net;
using System.Net.Sockets;
using Mortz.Server.Platform;

namespace Mortz.Platform.Steam;

// The addon owns this singleton; disposing the adapter only removes subscriptions.
public class GodotSteamServerApi : IDisposable
{
    public const int TicketCapacity = 1024;

    private readonly List<(StringName Signal, Callable Callback)> _subscriptions = new();
    private readonly GodotObject _server;
    private readonly Callable _connected;
    private readonly Callable _connectFailure;
    private readonly Callable _disconnected;
    private readonly Callable _validated;
    private bool _disposed;

    public event Action? Connected;
    public event Action<long, bool>? ConnectFailure;
    public event Action<long>? Disconnected;
    public event Action<ulong, long, ulong>? AuthTicketValidated;

    public GodotSteamServerApi()
    {
        if (!Engine.HasSingleton("SteamServer"))
        {
            throw new InvalidOperationException("GodotSteam SteamServer singleton is unavailable.");
        }
        _server = Engine.GetSingleton("SteamServer");
        _connected = Callable.From(() => Connected?.Invoke());
        _connectFailure = Callable.From<long, bool>((result, retrying) => ConnectFailure?.Invoke(result, retrying));
        _disconnected = Callable.From<long>(result => Disconnected?.Invoke(result));
        _validated = Callable.From<long, long, long>((account, result, owner) => AuthTicketValidated?.Invoke(unchecked((ulong)account), result, unchecked((ulong)owner)));
        try
        {
            Subscribe("server_connected", _connected);
            Subscribe("server_connect_failure", _connectFailure);
            Subscribe("server_disconnected", _disconnected);
            Subscribe("validate_auth_ticket_response", _validated);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Godot.Collections.Dictionary Initialize(string ip, ushort gamePort, ushort queryPort, string version, bool authenticate = true) =>
        _server.Call("serverInitEx", ip, gamePort, queryPort, authenticate ? 2 : 1, version).AsGodotDictionary();

    public void RunCallbacks() => _server.Call("run_callbacks");
    public bool HandleIncomingPacket(byte[] packet, string address, int port)
    {
        if (packet.Length is 0 or > 65507 || !IPAddress.TryParse(address, out IPAddress? source))
        {
            return false;
        }
        if (source.IsIPv4MappedToIPv6)
        {
            source = source.MapToIPv4();
        }
        if (source.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        return _server.Call("handleIncomingPacket", packet, source.ToString(), checked((ushort)port)).AsBool();
    }

    public ServerDatagram? TakeOutgoingPacket()
    {
        using Godot.Collections.Dictionary result = _server.Call("getNextOutgoingPacket").AsGodotDictionary();
        int length = result["length"].AsInt32();
        if (length == 0)
        {
            return null;
        }
        byte[] packet = result["out"].AsByteArray();
        if (length is < 0 or > 16384 || packet.Length != length)
        {
            throw new InvalidDataException("Steam returned an invalid outgoing packet length.");
        }
        return new ServerDatagram(packet, result["address"].AsString(), result["port"].AsInt32());
    }
    public void LogOnAnonymous() => _server.Call("logOnAnonymous");
    public bool LoggedOn() => _server.Call("loggedOn").AsBool();
    public ulong AccountId => unchecked((ulong)_server.Call("getSteamID").AsInt64());
    public int BeginAuthSession(byte[] ticket, ulong account)
    {
        if (ticket.Length is 0 or > TicketCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(ticket));
        }
        return _server.Call("beginAuthSession", ticket, ticket.Length, unchecked((long)account)).AsInt32();
    }
    public void EndAuthSession(ulong account) => _server.Call("endAuthSession", unchecked((long)account));
    public ulong CreateUnauthenticatedUserConnection() => unchecked((ulong)_server.Call("createUnauthenticatedUserConnection").AsInt64());
    public bool UpdateUserData(ulong account, string name, uint score) =>
        _server.Call("updateUserData", unchecked((long)account), name, score).AsBool();
    public void SetProduct(string product) => _server.Call("setProduct", product);
    public void SetGameDescription(string description) => _server.Call("setGameDescription", description);
    public void SetModDir(string directory) => _server.Call("setModDir", directory);
    public void SetDedicatedServer(bool dedicated) => _server.Call("setDedicatedServer", dedicated);
    public void SetServerName(string name) => _server.Call("setServerName", name);
    public void SetMapName(string map) => _server.Call("setMapName", map);
    public void SetMaxPlayerCount(int maximum) => _server.Call("setMaxPlayerCount", maximum);
    public void SetAdvertiseServerActive(bool active) => _server.Call("setAdvertiseServerActive", active);
    public void ClearAllKeyValues() => _server.Call("clearAllKeyValues");
    public void SetKeyValue(string key, string value) => _server.Call("setKeyValue", key, value);
    public void LogOff() => _server.Call("logOff");
    public void Shutdown() => _server.Call("serverShutdown");

    private void Subscribe(StringName signal, Callable callback)
    {
        var error = _server.Connect(signal, callback);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Cannot subscribe to SteamServer.{signal}: {error}");
        }
        _subscriptions.Add((signal, callback));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var (signal, callback) in _subscriptions)
        {
            _server.Disconnect(signal, callback);
        }
        _subscriptions.Clear();
    }
}
