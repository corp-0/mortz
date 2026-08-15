using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Net;

namespace Mortz.Client.Match;

/// <summary>Free-for-all score HUD: the local player's kills and deaths.</summary>
[Meta(typeof(IAutoNode))]
public partial class PlayerKillsHud : Control
{
    [Export] private Label _scoreLabel = null!;

    private bool _subscribed;

    [Dependency]
    public ClientMatchState MatchState => this.DependOn<ClientMatchState>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

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
        int localId = Network.LocalPeerId;
        _scoreLabel.Text =
            $"K {MatchState.Scores.Kills(localId)} / D {MatchState.Scores.Deaths(localId)}";
    }

    private void OnScoresChanged(MatchScoreSnapshot scores) => Render();
}
