using Godot;
using Mortz.Core.Net.Messages;

namespace Mortz.Client;

/// <summary>Screen-space local kills/deaths counter and winner banner.</summary>
public partial class ScoreOverlay : Control
{
    [Export] private Label _scoreLabel = null!;
    [Export] private Label _winnerLabel = null!;

    private readonly Dictionary<long, string> _names = new();
    private int _kills;
    private int _deaths;

    public override void _Ready()
    {
        RosterMsg.Received += OnRoster;
        EliminationMsg.Received += OnElimination;
        MatchEndMsg.Received += OnMatchEnd;
        UpdateScoreLabel();
    }

    public override void _ExitTree()
    {
        RosterMsg.Received -= OnRoster;
        EliminationMsg.Received -= OnElimination;
        MatchEndMsg.Received -= OnMatchEnd;
    }

    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        int count = Math.Min(msg.PeerIds.Length, msg.Names.Length);
        for (int i = 0; i < count; i++)
            _names[msg.PeerIds[i]] = msg.Names[i];
    }

    private void OnElimination(EliminationMsg msg)
    {
        long localId = Multiplayer.GetUniqueId();
        bool suicide = (msg.Flags & EliminationFlags.SUICIDE) != 0;
        if (!suicide && msg.KillerId == localId)
            _kills = msg.KillerKills;
        if (msg.VictimId == localId)
        {
            _deaths = msg.VictimDeaths;
            if (suicide)
                _kills = msg.KillerKills; // the suicide penalty may have moved it
        }
        UpdateScoreLabel();
    }

    private void OnMatchEnd(MatchEndMsg msg)
    {
        _winnerLabel.Text = msg.ByTeam ? $"Team {msg.WinnerId} wins!" : $"{Name(msg.WinnerId)} wins!";
        _winnerLabel.Visible = true;
    }

    private void UpdateScoreLabel() => _scoreLabel.Text = $"K {_kills} / D {_deaths}";

    private new string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";
}
