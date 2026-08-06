using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;
using Mortz.Server.Hosting;
using Mortz.Server.Pump;
using Mortz.Server.Query;

namespace Mortz.Server;

/// <summary>Composition root for the dedicated-server scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerMain : Node, IProvide<NetworkManager>
{
    [Export] private ServerHost _host = null!;
    [Export] private ServerPump _pump = null!;
    [Export] private ServerQueryResponder _query = null!;

    private NetworkManager _network = null!;

    NetworkManager IProvide<NetworkManager>.Value() => _network;

    public override void _Notification(int what) => this.Notify(what);

    partial void NotifyE2EListening();

    public void OnReady()
    {
        if (_host.Load == null)
        {
            GetTree().Quit(1);
            return;
        }

        _network = GetNode<NetworkManager>(NetworkManager.AUTOLOAD_PATH);
        this.Provide();
        if (!_host.Listen(_network))
        {
            GetTree().Quit(1);
            return;
        }
        NotifyE2EListening();
    }
}
