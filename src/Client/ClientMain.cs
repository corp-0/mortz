using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Roster;
using Mortz.Client.Score;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node,
    IProvide<ClientAdmin>,
    IProvide<ClientRoster>,
    IProvide<ClientStats>,
    IProvide<MatchSetup>,
    IProvide<MatchScore>
{
    [Export] private ClientAdmin _clientAdmin = null!;
    [Export] private ClientRoster _clientRoster = null!;
    [Export] private ClientStats _clientStats = null!;
    [Export] private MatchSetup _matchSetup = null!;
    [Export] private MatchScore _matchScore = null!;

    ClientAdmin IProvide<ClientAdmin>.Value() => _clientAdmin;
    ClientRoster IProvide<ClientRoster>.Value() => _clientRoster;
    ClientStats IProvide<ClientStats>.Value() => _clientStats;
    MatchSetup IProvide<MatchSetup>.Value() => _matchSetup;
    MatchScore IProvide<MatchScore>.Value() => _matchScore;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => this.Provide();
}
