using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Roster;
using Mortz.Client.Score;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Net;

namespace Mortz.Client.Session;

/// <summary>Owns the services that live for exactly one server connection:
/// created on connect, freed on disconnect, so a reconnect starts fresh.</summary>
[Meta(typeof(IAutoNode))]
public partial class ConnectedSession : Node,
    IProvide<MatchSetup>,
    IProvide<ClientStats>,
    IProvide<MatchScore>,
    IProvide<ClientRoster>,
    IProvide<ClientAdmin>,
    IProvide<INetwork>
{
    [Export] private MatchSetup _matchSetup = null!;
    [Export] private ClientStats _clientStats = null!;
    [Export] private MatchScore _matchScore = null!;
    [Export] private ClientRoster _clientRoster = null!;
    [Export] private ClientAdmin _clientAdmin = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    MatchSetup IProvide<MatchSetup>.Value() => _matchSetup;
    ClientStats IProvide<ClientStats>.Value() => _clientStats;
    MatchScore IProvide<MatchScore>.Value() => _matchScore;
    ClientRoster IProvide<ClientRoster>.Value() => _clientRoster;
    ClientAdmin IProvide<ClientAdmin>.Value() => _clientAdmin;
    INetwork IProvide<INetwork>.Value() => Network;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => this.Provide();
}
