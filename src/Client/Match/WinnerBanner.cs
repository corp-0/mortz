using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Match;

/// <summary>Mode-independent winner banner shown during the victory lap.</summary>
public partial class WinnerBanner : Control
{
    [Export] private Label _winnerLabel = null!;

    private readonly Dictionary<long, string> _names = new();

    public override void _Ready()
    {
        RosterMsg.Received += OnRoster;
        MatchProtocol.MatchEnded += OnMatchEnd;
    }

    public override void _ExitTree()
    {
        RosterMsg.Received -= OnRoster;
        MatchProtocol.MatchEnded -= OnMatchEnd;
    }

    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        int count = Math.Min(msg.PeerIds.Length, msg.Names.Length);
        for (int i = 0; i < count; i++)
        {
            _names[msg.PeerIds[i]] = msg.Names[i];
        }
    }

    private void OnMatchEnd(Victor winner)
    {
        _winnerLabel.Text = $"{Describe(winner)} wins!";
        _winnerLabel.Visible = true;
    }

    private string Describe(Victor winner) => winner switch
    {
        TeamVictor team => Teams.Name(team.Team),
        PlayerVictor player => Name(player.PeerId),
        _ => throw new ArgumentOutOfRangeException(nameof(winner)),
    };

    private new string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";
}
