using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Ui;

namespace Mortz.Client.Match;

/// <summary>Picks which score HUD is alive from the match rules.</summary>
[Meta(typeof(IAutoNode))]
public partial class ScoreHudHost : ViewVariantHost<ScoreHudKind>
{
    [Export] private PackedScene _playerKills = null!;
    [Export] private PackedScene _teamKills = null!;

    private bool _subscribed;

    [Dependency]
    public MatchSetup Setup => this.DependOn<MatchSetup>();

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

    protected override ScoreHudKind CurrentKey() =>
        Setup.Rules.Teams ? ScoreHudKind.TEAM_KILLS : ScoreHudKind.PLAYER_KILLS;

    protected override PackedScene SceneFor(ScoreHudKind key) =>
        key == ScoreHudKind.TEAM_KILLS ? _teamKills : _playerKills;
}
