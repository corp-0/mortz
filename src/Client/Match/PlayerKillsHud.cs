using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Score;
using Mortz.Net;

namespace Mortz.Client.Match;

/// <summary>Free-for-all score HUD: the local player's kills and deaths.</summary>
[Meta(typeof(IAutoNode))]
public partial class PlayerKillsHud : Control
{
    [Export] private Label _scoreLabel = null!;

    private bool _subscribed;

    [Dependency]
    public MatchScore Score => this.DependOn<MatchScore>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

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
        long localId = Network.LocalPeerId;
        _scoreLabel.Text = $"K {Score.Kills(localId)} / D {Score.Deaths(localId)}";
    }
}
