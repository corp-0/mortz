using System.Reflection;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;
using Mortz.Protocol.Net.Admission;
using Mortz.Server.Admission;
using Mortz.Server.Diagnostics;
using Mortz.Server.Hosting;
using Mortz.Shared.Logging;

namespace Mortz.Server.Pump;

/// <summary>The only file that connects the engine to server logic.</summary>
[Meta(typeof(IAutoNode))]
[GlobalClass]
public partial class ServerPump : Node
{
    [Dependency] private NetworkManager Network => this.DependOn<NetworkManager>();

    private NetworkManager? _network;
    private ServerAdmission? _admission;

    public GameServer Server { get; private set; } = null!;

    public override void _Notification(int what) => this.Notify(what);

    partial void AttachE2E(ref IMatchObserver observer, ref IMatchControl control);

    public GameServer Start(ServerBootLoad load, IAdmissionVerifier verifier)
    {
        NetworkManager network = Network;
        _network = network;
        IMatchObserver observer = new NullMatchObserver();
        IMatchControl control = new NullMatchControl();
        AttachE2E(ref observer, ref control);
        string version = typeof(ServerPump).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Server = new GameServer(load.Boot, new GodotTransport(network),
            new GodotMapSource(load.Content), MortzLog.For("server"),
            observer, control, version);
        _admission = new ServerAdmission(verifier, network, Time.GetTicksMsec);
        _admission.Admitted += Server.Connect;
        _admission.Departed += Server.Disconnect;
        network.TransportPeerConnected += Connected;
        network.TransportPeerDisconnected += _admission.Disconnected;
        network.HelloReceived += Hello;
        network.ProofReceived += Proof;
        network.InputsReceived += Server.Inputs;
        network.ServerSink = Server.Receive;
        return Server;
    }

    public void Stop()
    {
        if (_network == null)
            return;
        _network.TransportPeerConnected -= Connected;
        _network.TransportPeerDisconnected -= _admission!.Disconnected;
        _network.HelloReceived -= Hello;
        _network.ProofReceived -= Proof;
        _admission.Dispose();
        _admission = null;
        _network.InputsReceived -= Server.Inputs;
        _network.ServerSink = null;
        _network = null;
        Server.Dispose();
    }

    private void Connected(int peerId) => _admission?.Connected(peerId, Time.GetTicksMsec());
    private void Hello(int peerId, AdmissionHello hello) => _admission?.Hello(peerId, hello, Time.GetTicksMsec());
    private void Proof(int peerId, SteamProof proof) => _admission?.Proof(peerId, proof, Time.GetTicksMsec());
    public void AdvanceAdmission() => _admission?.Advance(Time.GetTicksMsec());

    public override void _PhysicsProcess(double delta)
    {
        if (_admission != null)
            Server.Advance(new ServerTime(Time.GetTicksMsec(), delta));
    }
}
