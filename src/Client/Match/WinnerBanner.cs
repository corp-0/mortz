using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;

namespace Mortz.Client.Match;

/// <summary>Mode-independent winner banner shown during the victory lap.</summary>
[Meta(typeof(IAutoNode))]
public partial class WinnerBanner : Control
{
    [Export] private Label _winnerLabel = null!;

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private ClientMatchState MatchState => this.DependOn<ClientMatchState>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        MatchState.WinnerChanged += OnWinnerChanged;
        if (MatchState.Winner is Victor winner)
            OnWinnerChanged(winner);
    }

    public void OnExitTree()
    {
        MatchState.WinnerChanged -= OnWinnerChanged;
    }

    private void OnWinnerChanged(Victor? winner)
    {
        if (winner == null)
            return;
        _winnerLabel.Text = $"{Describe(winner)} wins!";
        _winnerLabel.Visible = true;
    }

    private string Describe(Victor winner) => winner switch
    {
        Victor.Team team => Teams.Name(team.Value),
        Victor.Player player => Players.NameOf(player.PeerId),
        _ => throw new ArgumentOutOfRangeException(nameof(winner)),
    };
}
