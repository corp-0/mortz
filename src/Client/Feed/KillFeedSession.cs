using Mortz.Core.Net.Messages;

namespace Mortz.Client.Feed;

/// <summary>Turns the authoritative elimination stream into display lines.
/// Stateless besides its subscriptions: consumers keep whatever history they
/// show, so there is nothing to reset at session boundaries.</summary>
public sealed class KillFeedSession : IDisposable
{
    private readonly Func<long, string> _name;
    private bool _subscribed;

    public KillFeedSession(Func<long, string> name) => _name = name;

    public event Action<string>? LineAdded;

    public void Subscribe()
    {
        if (_subscribed)
            return;
        EliminationMsg.Received += OnElimination;
        MatchEndMsg.Received += OnMatchEnd;
        _subscribed = true;
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            EliminationMsg.Received -= OnElimination;
            MatchEndMsg.Received -= OnMatchEnd;
            _subscribed = false;
        }
    }

    private void OnElimination(EliminationMsg message) =>
        LineAdded?.Invoke(EliminationText.Format(message, _name));

    private void OnMatchEnd(MatchEndMsg message)
    {
        string winner = message.ByTeam ? $"Team {message.WinnerId}" : _name(message.WinnerId);
        LineAdded?.Invoke($"{winner} wins!");
    }
}
