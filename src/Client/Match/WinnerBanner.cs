using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Roster;
using Mortz.Core.Match;
using Mortz.Core.Net;

namespace Mortz.Client.Match;

/// <summary>Mode-independent winner banner shown during the victory lap.</summary>
[Meta(typeof(IAutoNode))]
public partial class WinnerBanner : Control
{
    [Export] private Label _winnerLabel = null!;

    [Dependency]
    private MatchRoster Roster => this.DependOn<MatchRoster>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => MatchProtocol.MatchEnded += OnMatchEnd;

    public void OnExitTree() => MatchProtocol.MatchEnded -= OnMatchEnd;

    private void OnMatchEnd(Victor winner)
    {
        _winnerLabel.Text = $"{Describe(winner)} wins!";
        _winnerLabel.Visible = true;
    }

    private string Describe(Victor winner) => winner switch
    {
        TeamVictor team => Teams.Name(team.Team),
        PlayerVictor player => Roster.NameOf(player.PeerId),
        _ => throw new ArgumentOutOfRangeException(nameof(winner)),
    };
}
