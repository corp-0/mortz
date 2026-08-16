using Mortz.Server.Diagnostics;

namespace Mortz.Server.Match.Services;

public class MatchUpdateObserver(IMatchObserver observer) : IObserveMatchUpdate
{
    public void MatchUpdated(in MatchUpdate update, ServerTime time) =>
        observer.MatchAdvanced(update);
}
