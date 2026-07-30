using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Roster;
using Mortz.Client.Score;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client.Session;

/// <summary>Owns the services that live for exactly one server connection:
/// created on connect, freed on disconnect, so a reconnect starts fresh.</summary>
[Meta(typeof(IAutoNode))]
public partial class ConnectedSession : Node,
    IProvide<MatchSetup>,
    IProvide<ClientStats>,
    IProvide<MatchScore>,
    IProvide<MatchRoster>,
    IProvide<ClientAdmin>
{
    [Export] private MatchSetup _matchSetup = null!;
    [Export] private ClientStats _clientStats = null!;
    [Export] private MatchScore _matchScore = null!;
    [Export] private MatchRoster _matchRoster = null!;
    [Export] private ClientAdmin _clientAdmin = null!;

    MatchSetup IProvide<MatchSetup>.Value() => _matchSetup;
    ClientStats IProvide<ClientStats>.Value() => _clientStats;
    MatchScore IProvide<MatchScore>.Value() => _matchScore;
    MatchRoster IProvide<MatchRoster>.Value() => _matchRoster;
    ClientAdmin IProvide<ClientAdmin>.Value() => _clientAdmin;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => this.Provide();
}
