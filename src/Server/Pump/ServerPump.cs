using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;
using Mortz.Server.Diagnostics;
using Mortz.Server.Hosting;
using Mortz.Shared.Logging;

namespace Mortz.Server.Pump;

/// <summary>The only file that connects the engine to server logic.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerPump : Node
{
    [Export] private ServerHost _host = null!;

    [Dependency]
    private NetworkManager Network => this.DependOn<NetworkManager>();

    public GameServer Server { get; private set; } = null!;

    public override void _Notification(int what) => this.Notify(what);

    partial void AttachE2E(ref IMatchObserver observer, ref IMatchControl control);

    public void OnResolved()
    {
        ServerBootLoad load = _host.Load!.Value;
        IMatchObserver observer = new NullMatchObserver();
        IMatchControl control = new NullMatchControl();
        AttachE2E(ref observer, ref control);
        Server = new GameServer(load.Boot, new GodotTransport(Network),
            new GodotMapSource(load.Content), MortzLog.For("server"),
            observer, control);
        Network.PeerJoined += Server.Connect;
        Network.PeerLeft += Server.Disconnect;
        Network.InputsReceived += Server.Inputs;
        Network.ServerSink = Server.Receive;
    }

    public void OnExitTree() => Server.Dispose();

    public override void _PhysicsProcess(double delta) =>
        Server.Advance(new ServerTime(Time.GetTicksMsec(), delta));
}
