using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node,
    IProvide<NetworkManager>,
    IProvide<INetwork>
{
    private NetworkManager _network = null!;

    NetworkManager IProvide<NetworkManager>.Value() => _network;
    INetwork IProvide<INetwork>.Value() => _network;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _network = GetNode<NetworkManager>(NetworkManager.AUTOLOAD_PATH);
        this.Provide();
    }
}
