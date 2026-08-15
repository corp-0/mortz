using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Core.Net;
using Mortz.Net;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node,
    IProvide<NetworkManager>,
    IProvide<INetwork>,
    IProvide<IClientSender>,
    IProvide<NetRouter>,
    IProvide<ISfx>
{
    [Export] private Sfx _sfx = null!;

    private NetworkManager _network = null!;

    NetworkManager IProvide<NetworkManager>.Value() => _network;
    INetwork IProvide<INetwork>.Value() => _network;
    IClientSender IProvide<IClientSender>.Value() => _network;
    NetRouter IProvide<NetRouter>.Value() => _network.Router;
    ISfx IProvide<ISfx>.Value() => _sfx;

    public override void _Notification(int what) => this.Notify(what);

    partial void OnToolsReady();

    public void OnReady()
    {
        _network = GetNode<NetworkManager>(NetworkManager.AUTOLOAD_PATH);
        OnToolsReady();
        this.Provide();
    }
}
