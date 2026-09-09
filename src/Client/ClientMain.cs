using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Servers;
using Mortz.Client.Session;
using Mortz.Client.Settings;
using Mortz.Net;
using Mortz.Protocol.Net;
#if MORTZ_STEAM
using Mortz.Platform;
using Mortz.Platform.Steam;
using Mortz.Shared;
#endif

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node,
    IProvide<INetwork>,
    IProvide<IClientSender>,
    IProvide<NetRouter>,
    IProvide<ISfx>,
    IProvide<ClientSettings>,
    IProvide<IClientTicketProvider>, IProvide<IInternetDiscovery>
{
    [Export] private Sfx _sfx = null!;
    private ClientSettings _settings = null!;
    private readonly IInternetDiscovery _discovery = new UnavailableInternetDiscovery();
    private readonly IClientTicketProvider _tickets = new GuestTicketProvider();
#if MORTZ_STEAM
    private readonly SteamClientRuntime _steam = new();
#endif

    [Dependency] private NetworkManager Network => this.DependOn<NetworkManager>();
    INetwork IProvide<INetwork>.Value() => Network;
    IClientSender IProvide<IClientSender>.Value() => Network;
    NetRouter IProvide<NetRouter>.Value() => Network.Router;
    ISfx IProvide<ISfx>.Value() => _sfx;
    ClientSettings IProvide<ClientSettings>.Value() => _settings;
    IClientTicketProvider IProvide<IClientTicketProvider>.Value() => _tickets;

    IInternetDiscovery IProvide<IInternetDiscovery>.Value() => _discovery;

    public override void _Notification(int what) => this.Notify(what);

    partial void OnToolsReady();

    public void OnResolved()
    {
#if MORTZ_STEAM
        _steam.Start();
        _tickets = _steam.Tickets;
        _discovery = _steam.Discovery;
        _settings = ClientSettings.Load(MortzUserData.Resolve(), _steam.PersonaName);
        PlatformRuntimeOwner owner = new();
        owner.Initialize(_steam.Advance, _steam.Dispose);
        AddChild(owner);
        // Release connection and browser consumers before the addon singleton.
        MoveChild(owner, 0);
#else
        _settings = ClientSettings.Load();
#endif
        OnToolsReady();
        this.Provide();
    }

    public void OnExitTree()
    {
#if MORTZ_STEAM
        _steam.Dispose();
#endif
    }
}
