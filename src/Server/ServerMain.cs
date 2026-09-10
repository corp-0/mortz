using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;
using Mortz.Platform;
using Mortz.Protocol.Hosting;
using Mortz.Server.Admission;
using Mortz.Server.Hosting;
using Mortz.Server.Platform;
using Mortz.Server.Pump;
using Mortz.Server.Query;
using Mortz.Shared;
using Mortz.Shared.Logging;
#if TOOLS
using Mortz.Shared.E2E;
#endif
#if MORTZ_STEAM
using Mortz.Platform.Steam;
#endif

namespace Mortz.Server;

[Meta(typeof(IAutoNode))]
public partial class ServerMain : Node
{
    [Export] private ServerHost _host = null!;
    [Export] private ServerPump _pump = null!;
    [Export] private ServerQueryResponder _query = null!;

    [Dependency] private NetworkManager Network => this.DependOn<NetworkManager>();
    private ServerRuntime? _runtime;
    private GameServer _server = null!;
    private ServerCapabilities _lastCapabilities;
    private OwnedServerConnection? _owner;
    private Task? _startupReport;
    private bool _startupFailed;

    public ServerCapabilities Capabilities => _runtime?.Capabilities ?? default;
    public int BoundQueryPort => Capabilities.Query == QueryOwner.NONE ? -1 : _host.Load!.Value.Boot.QueryPort;
    public override void _Notification(int what) => this.Notify(what);
    partial void NotifyE2EListening();

    public void OnResolved()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            string? pipeName = CmdArgs.GetValue("--host-pipe");
            string? token = CmdArgs.GetValue("--host-token");
            if (CmdArgs.HasFlag("--host-pipe") || CmdArgs.HasFlag("--host-token"))
            {
                _owner = new OwnedServerConnection(pipeName ?? "", token ?? "");
            }
            StartRuntime();
        }
        catch (Exception exception)
        {
            MortzLog.For("server").Error(exception, "server startup failed");
            FailStartup("Local server startup failed. Check the server log.");
        }
    }

    private void StartRuntime()
    {
        if (_host.Load == null || !_host.Listen(Network))
        {
            FailStartup(_host.Load == null
                ? "Local server could not load its content or settings. Check the server log."
                : "Local server could not bind its game port. It may already be in use.");
            return;
        }
        ServerBoot boot = _host.Load.Value.Boot;
        IAdmissionVerifier verifier = new GuestAdmissionVerifier();
        IServerPlatform? platform = null;
        IServerPacketRouter? packetRouter = null;
#if MORTZ_STEAM
        SteamServerRuntime steam = new(() => _server.PublicationPlayers);
        platform = steam;
        verifier = steam.Verification;
        if (!boot.SteamPublic)
        {
            packetRouter = steam;
        }
#endif
        _server = _pump.Start(_host.Load.Value, Network, verifier);
        _runtime = new ServerRuntime(platform,
            port => _query.Start(port, _server.Describe,
#if TOOLS
                E2ELaunch.Enabled ? "127.0.0.1" : "*",
#else
                "*",
#endif
                packetRouter), _query.Stop);
        _runtime.Start(_server.Describe(), boot.QueryPort, boot.SteamPublic);
        // Children leave in reverse order; release Steam before the query node closes its socket.
        PlatformRuntimeOwner lifetime = new();
        lifetime.Initialize(AdvanceRuntime, Stop);
        AddChild(lifetime);
        ReportCapabilities();
        _startupReport = _owner?.ReportAsync(new(HostControlKind.READY, "", "127.0.0.1",
            boot.GamePort, BoundQueryPort));
        NotifyE2EListening();
    }

    private void FailStartup(string reason)
    {
        _startupFailed = true;
        _startupReport = _owner?.ReportAsync(new(HostControlKind.FAILED, "", Reason: reason));
        if (_owner == null)
        {
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        if (_owner?.StopRequested == true || (_startupFailed && _startupReport?.IsCompleted != false))
        {
            GetTree().Quit(_startupFailed ? 1 : 0);
        }
    }

    private void AdvanceRuntime()
    {
        _pump.AdvanceAdmission();
        _runtime?.Advance(_server.Describe());
        _pump.AdvanceAdmission();
        ReportCapabilities();
    }

    private void ReportCapabilities()
    {
        if (_runtime == null || Capabilities == _lastCapabilities)
        {
            return;
        }
        _lastCapabilities = Capabilities;
        MortzLog.For("server").Information(
            "game={Game} query={Query} authentication={Authentication} publication={Publication}: {Status}",
            Capabilities.GameListener, Capabilities.Query, Capabilities.Authentication,
            Capabilities.Publication, Capabilities.Status);
    }

    public void OnExitTree()
    {
        _owner?.Dispose();
        Stop();
    }

    private void Stop()
    {
        _pump.Stop();
        _runtime?.Dispose();
        Network.Shutdown();
    }
}
