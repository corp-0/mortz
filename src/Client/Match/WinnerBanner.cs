using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;

namespace Mortz.Client.Match;

/// <summary>Mode-independent winner banner shown during the victory lap.</summary>
[Meta(typeof(IAutoNode))]
public partial class WinnerBanner : Control, IHandle<MatchEndMsg>
{
    [Export] private Label _winnerLabel = null!;

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public void Handle(in MatchEndMsg message)
    {
        if (!MatchProtocol.TryDecode(message, out Victor? winner))
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
