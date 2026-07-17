using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Chat;
using Mortz.Client.Score;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client;

/// <summary>Composition root for the client scene.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMain : Node,
    IProvide<IClientChat>, IProvide<IClientStats>, IProvide<IMatchSetup>, IProvide<IMatchScore>
{
    [Export] private ClientChat _clientChat = null!;
    [Export] private ClientStats _clientStats = null!;
    [Export] private MatchSetup _matchSetup = null!;
    [Export] private MatchScore _matchScore = null!;

    IClientChat IProvide<IClientChat>.Value() => _clientChat;
    IClientStats IProvide<IClientStats>.Value() => _clientStats;
    IMatchSetup IProvide<IMatchSetup>.Value() => _matchSetup;
    IMatchScore IProvide<IMatchScore>.Value() => _matchScore;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => this.Provide();
}
