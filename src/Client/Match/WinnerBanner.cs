using Godot;
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
        MatchEndMsg.Received += OnMatchEnd;
    }

    public override void _ExitTree()
    {
        RosterMsg.Received -= OnRoster;
        MatchEndMsg.Received -= OnMatchEnd;
    }

    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        int count = Math.Min(msg.PeerIds.Length, msg.Names.Length);
        for (int i = 0; i < count; i++)
            _names[msg.PeerIds[i]] = msg.Names[i];
    }

    private void OnMatchEnd(MatchEndMsg msg)
    {
        _winnerLabel.Text = msg.ByTeam ? $"Team {msg.WinnerId} wins!" : $"{Name(msg.WinnerId)} wins!";
        _winnerLabel.Visible = true;
    }

    private new string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";
}
