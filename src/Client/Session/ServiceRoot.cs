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

/// <summary>Provides a chosen set of client services to whatever is mounted
/// under it.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServiceRoot : Node,
    IProvide<MatchSetup>,
    IProvide<ClientStats>,
    IProvide<MatchScore>,
    IProvide<MatchRoster>,
    IProvide<ClientAdmin>,
    IProvide<INetwork>,
    IProvide<ISessionExit>
{
    public MatchSetup Setup { get; set; } = null!;
    public ClientStats Stats { get; set; } = null!;
    public MatchScore Score { get; set; } = null!;
    public MatchRoster Roster { get; set; } = null!;
    public ClientAdmin Admin { get; set; } = null!;
    public INetwork Network { get; set; } = null!;
    public ISessionExit SessionExit { get; set; } = null!;

    MatchSetup IProvide<MatchSetup>.Value() => Setup;
    ClientStats IProvide<ClientStats>.Value() => Stats;
    MatchScore IProvide<MatchScore>.Value() => Score;
    MatchRoster IProvide<MatchRoster>.Value() => Roster;
    ClientAdmin IProvide<ClientAdmin>.Value() => Admin;
    INetwork IProvide<INetwork>.Value() => Network;
    ISessionExit IProvide<ISessionExit>.Value() => SessionExit;

    public override void _Notification(int what) => this.Notify(what);
    public override void _Ready() => this.Provide();
}
