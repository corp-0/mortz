using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Score;
using Mortz.Client.Ui;

namespace Mortz.Client.Match;

/// <summary>Team score HUD: team vs team kill totals in the team colors.</summary>
[Meta(typeof(IAutoNode))]
public partial class TeamKillsHud : Control
{
    [Export] private Label _team1Label = null!;
    [Export] private Label _team2Label = null!;

    private bool _subscribed;

    [Dependency]
    public IMatchScore Score => this.DependOn<IMatchScore>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _team1Label.AddThemeColorOverride("font_color", TeamColors.Team1);
        _team2Label.AddThemeColorOverride("font_color", TeamColors.Team2);
    }

    public void OnResolved()
    {
        Score.Changed += Render;
        _subscribed = true;
        Render();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Score.Changed -= Render;
        _subscribed = false;
    }

    // A just-swapped-out view can still get the swap's own event: skip it.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        _team1Label.Text = Score.TeamKills(1).ToString();
        _team2Label.Text = Score.TeamKills(2).ToString();
    }
}
