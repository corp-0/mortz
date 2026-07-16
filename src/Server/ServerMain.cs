using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

namespace Mortz.Server;

/// <summary>Composition root for the dedicated-server scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerMain : Node, IProvide<ServerHost>, IProvide<IServerSession>
{
    [Export] private ServerHost _host = null!;
    [Export] private ServerSessionController _session = null!;

    ServerHost IProvide<ServerHost>.Value() => _host;
    IServerSession IProvide<IServerSession>.Value() => _session;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        if (!_host.IsConfigured)
        {
            GetTree().Quit(1);
            return;
        }

        this.Provide();
        if (!_host.Listen())
            GetTree().Quit(1);
    }
}
