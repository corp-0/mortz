using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Ui;
using Mortz.Core.Match.Teams;

namespace Mortz.Client.Match;

/// <summary>Team score HUD: team vs team kill totals in the team colors.</summary>
[Meta(typeof(IAutoNode))]
public partial class TeamKillsHud : Control
{
    [Export] private Label _team1Label = null!;
    [Export] private Label _team2Label = null!;

    private bool _subscribed;

    [Dependency]
    public ClientMatchState MatchState => this.DependOn<ClientMatchState>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _team1Label.AddThemeColorOverride("font_color", TeamColors.Blue);
        _team2Label.AddThemeColorOverride("font_color", TeamColors.Red);
    }

    public void OnResolved()
    {
        MatchState.ScoresChanged += OnScoresChanged;
        _subscribed = true;
        Render();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        MatchState.ScoresChanged -= OnScoresChanged;
        _subscribed = false;
    }

    // A just-swapped-out view can still get the swap's own event: skip it.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        _team1Label.Text = MatchState.Scores.TeamKills[Team.BLUE].ToString();
        _team2Label.Text = MatchState.Scores.TeamKills[Team.RED].ToString();
    }

    private void OnScoresChanged(MatchScoreSnapshot scores) => Render();
}
