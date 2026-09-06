using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Players;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Net;
using Mortz.Protocol.Net;

namespace Mortz.Client.Session;

/// <summary>Owns the services that live for exactly one server connection:
/// created on connect, freed on disconnect, so a reconnect starts fresh.</summary>
[Meta(typeof(IAutoNode))]
public partial class ConnectedSession : Node,
    IProvide<MatchSetup>,
    IProvide<Pings>,
    IProvide<SessionWins>,
    IProvide<ClientPlayers>,
    IProvide<ClientAdmin>, IProvide<ClientChat>
{

    MatchSetup IProvide<MatchSetup>.Value() => _runtime.Setup;
    Pings IProvide<Pings>.Value() => _runtime.Pings;
    SessionWins IProvide<SessionWins>.Value() => _runtime.Wins;
    ClientPlayers IProvide<ClientPlayers>.Value() => _runtime.Players;

    /// <summary>For the session controller, which owns match lifecycle.</summary>
    public ClientConnectionScope Connection => _runtime;

    public ClientPlayers Players => _runtime.Players;

    /// <summary>For authenticating the player who launched a local server.</summary>
    public ClientAdmin Admin => _runtime.Admin;

    ClientAdmin IProvide<ClientAdmin>.Value() => _runtime.Admin;

    ClientChat IProvide<ClientChat>.Value() => _runtime.Chat;

    public override void _Notification(int what) => this.Notify(what);

    [Dependency] private NetRouter Router => this.DependOn<NetRouter>();
    [Dependency] private IClientSender Sender => this.DependOn<IClientSender>();
    [Dependency] private INetwork Network => this.DependOn<INetwork>();
    [Dependency] private ISessionExit SessionExit => this.DependOn<ISessionExit>();
    private ClientConnectionScope _runtime = null!;

    public void OnResolved()
    {
        _runtime = new ClientConnectionScope(Router, Sender, () => Network.LocalPeerId, SessionExit);
        this.Provide();
    }

    public void OnExitTree() => _runtime?.Dispose();
}
