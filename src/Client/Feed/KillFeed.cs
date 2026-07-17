using Godot;
using Mortz.Client.Roster;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Feed;

/// <summary>Turns the authoritative elimination stream into display lines.
/// Stateless besides its subscriptions: consumers keep whatever history they
/// show, so there is nothing to reset at session boundaries.</summary>
public partial class KillFeed : Node
{
    [Export] private ClientRoster _roster = null!;

    public event Action<string>? LineAdded;

    public override void _Ready()
    {
        EliminationMsg.Received += OnElimination;
        MatchEndMsg.Received += OnMatchEnd;
    }

    public override void _ExitTree()
    {
        EliminationMsg.Received -= OnElimination;
        MatchEndMsg.Received -= OnMatchEnd;
    }

    private void OnElimination(EliminationMsg message) =>
        LineAdded?.Invoke(EliminationText.Format(message, _roster.NameOf));

    private void OnMatchEnd(MatchEndMsg message)
    {
        string winner = message.ByTeam
            ? $"Team {message.WinnerId}"
            : _roster.NameOf(message.WinnerId);
        LineAdded?.Invoke($"{winner} wins!");
    }
}
