using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Players;
using Mortz.Client.Score;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Core.Net;
using Mortz.Net;

namespace Mortz.Client.Session;

/// <summary>Provides a chosen set of client services to whatever is mounted
/// under it.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServiceRoot : Node,
    IProvide<MatchSetup>,
    IProvide<Pings>,
    IProvide<SessionWins>,
    IProvide<MatchScore>,
    IProvide<ClientPlayers>,
    IProvide<ClientAdmin>,
    IProvide<INetwork>,
    IProvide<NetRouter>,
    IProvide<ISessionExit>
{
    public MatchSetup Setup { get; set; } = null!;
    public Pings Pings { get; set; } = null!;
    public SessionWins Wins { get; set; } = null!;
    public MatchScore Score { get; set; } = null!;
    public ClientPlayers Players { get; set; } = null!;
    public ClientAdmin Admin { get; set; } = null!;
    public INetwork Network { get; set; } = null!;
    public NetRouter Router { get; set; } = null!;
    public ISessionExit SessionExit { get; set; } = null!;

    MatchSetup IProvide<MatchSetup>.Value() => Setup;
    Pings IProvide<Pings>.Value() => Pings;
    SessionWins IProvide<SessionWins>.Value() => Wins;
    MatchScore IProvide<MatchScore>.Value() => Score;
    ClientPlayers IProvide<ClientPlayers>.Value() => Players;
    ClientAdmin IProvide<ClientAdmin>.Value() => Admin;
    INetwork IProvide<INetwork>.Value() => Network;
    NetRouter IProvide<NetRouter>.Value() => Router;
    ISessionExit IProvide<ISessionExit>.Value() => SessionExit;

    public override void _Notification(int what) => this.Notify(what);
    public override void _Ready() => this.Provide();
}
