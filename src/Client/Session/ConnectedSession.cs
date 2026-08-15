using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Players;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client.Session;

/// <summary>Owns the services that live for exactly one server connection:
/// created on connect, freed on disconnect, so a reconnect starts fresh.</summary>
[Meta(typeof(IAutoNode))]
public partial class ConnectedSession : Node,
    IProvide<MatchSetup>,
    IProvide<Pings>,
    IProvide<SessionWins>,
    IProvide<ClientPlayers>,
    IProvide<ClientAdmin>
{
    [Export] private MatchSetup _matchSetup = null!;
    [Export] private Pings _pings = null!;
    [Export] private SessionWins _sessionWins = null!;
    [Export] private ClientPlayers _clientPlayers = null!;
    [Export] private ClientAdmin _clientAdmin = null!;

    MatchSetup IProvide<MatchSetup>.Value() => _matchSetup;
    Pings IProvide<Pings>.Value() => _pings;
    SessionWins IProvide<SessionWins>.Value() => _sessionWins;
    ClientPlayers IProvide<ClientPlayers>.Value() => _clientPlayers;

    /// <summary>For the session controller, which owns match lifecycle.</summary>
    public ClientPlayers Players => _clientPlayers;
    ClientAdmin IProvide<ClientAdmin>.Value() => _clientAdmin;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => this.Provide();
}
