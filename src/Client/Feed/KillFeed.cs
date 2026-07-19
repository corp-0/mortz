using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Roster;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Feed;

/// <summary>Turns the authoritative elimination stream into display lines.</summary>
[Meta(typeof(IAutoNode))]
public partial class KillFeed : Node, IKillFeed
{
    private bool _subscribed;

    public event Action<string>? LineAdded;

    [Dependency]
    private ClientRoster Roster => this.DependOn<ClientRoster>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        EliminationMsg.Received += OnElimination;
        MatchEndMsg.Received += OnMatchEnd;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        EliminationMsg.Received -= OnElimination;
        MatchEndMsg.Received -= OnMatchEnd;
        _subscribed = false;
    }

    private void OnElimination(EliminationMsg message) =>
        LineAdded?.Invoke(EliminationText.Format(message, Roster.NameOf));

    private void OnMatchEnd(MatchEndMsg message)
    {
        string winner = message.ByTeam
            ? $"Team {message.WinnerId}"
            : Roster.NameOf(message.WinnerId);
        LineAdded?.Invoke($"{winner} wins!");
    }
}
