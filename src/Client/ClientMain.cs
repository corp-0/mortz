using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Score;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node,
    IProvide<ClientChat>,
    IProvide<ClientAdmin>,
    IProvide<ClientStats>,
    IProvide<MatchSetup>,
    IProvide<MatchScore>
{
    [Export] private ClientChat _clientChat = null!;
    [Export] private ClientAdmin _clientAdmin = null!;
    [Export] private ClientStats _clientStats = null!;
    [Export] private MatchSetup _matchSetup = null!;
    [Export] private MatchScore _matchScore = null!;

    ClientChat IProvide<ClientChat>.Value() => _clientChat;
    ClientAdmin IProvide<ClientAdmin>.Value() => _clientAdmin;
    ClientStats IProvide<ClientStats>.Value() => _clientStats;
    MatchSetup IProvide<MatchSetup>.Value() => _matchSetup;
    MatchScore IProvide<MatchScore>.Value() => _matchScore;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => this.Provide();
}
