using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Ui;

namespace Mortz.Client.Menus;

/// <summary>Picks which lobby roster layout is alive from the Teams rule.</summary>
[Meta(typeof(IAutoNode))]
public partial class RosterHost : ViewVariantHost<RosterLayout>
{
    [Export] private PackedScene _single = null!;
    [Export] private PackedScene _teamColumns = null!;

    private bool _subscribed;

    [Dependency]
    public IMatchSetup Setup => this.DependOn<IMatchSetup>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Setup.TeamsChanged += Refresh;
        _subscribed = true;
        Refresh();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Setup.TeamsChanged -= Refresh;
        _subscribed = false;
    }

    protected override RosterLayout CurrentKey() =>
        Setup.Rules.Teams ? RosterLayout.TEAM_COLUMNS : RosterLayout.SINGLE;

    protected override PackedScene SceneFor(RosterLayout key) =>
        key == RosterLayout.TEAM_COLUMNS ? _teamColumns : _single;
}
