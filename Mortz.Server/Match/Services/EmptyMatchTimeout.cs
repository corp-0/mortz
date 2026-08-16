using Mortz.Server.Phases;

namespace Mortz.Server.Match.Services;

public class EmptyMatchTimeout(
    MatchRuntime runtime,
    ServerClock clock)
    : IObserveMatchRoster, IAdvanceMatch
{
    private const ulong TIMEOUT_MS = 10_000;
    private ulong? _emptySinceMs;

    public PhaseRequest Advance(ServerTime time)
    {
        if (_emptySinceMs is not ulong emptySince ||
            time.Ms < emptySince ||
            time.Ms - emptySince < TIMEOUT_MS)
        {
            return PhaseRequest.NONE;
        }

        return PhaseRequest.RETURN_TO_LOBBY;
    }

    public void RosterChanged()
    {
        if (runtime.SeatedPlayerCount > 0)
        {
            _emptySinceMs = null;
            return;
        }

        // Join-in-progress spectators do not restart an existing timeout.
        _emptySinceMs ??= clock.Ms;
    }
}
