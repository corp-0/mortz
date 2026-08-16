using Mortz.Server.Content;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Services;

/// <summary>The services whose lifetime is exactly one match.</summary>
public sealed class MatchServices
{
    private readonly IObserveMatchRoster[] _rosterObservers;
    private readonly IEnterMatch[] _entrants;
    private readonly IObserveMatchInput[] _inputObservers;
    private readonly IObserveMatchUpdate[] _updateObservers;
    private readonly IAdvanceMatch[] _advance;

    private MatchServices(IReadOnlyList<IMatchService> all)
    {
        All = all;
        _rosterObservers = [.. all.OfType<IObserveMatchRoster>()];
        _entrants = [.. all.OfType<IEnterMatch>()];
        _inputObservers = [.. all.OfType<IObserveMatchInput>()];
        _updateObservers = [.. all.OfType<IObserveMatchUpdate>()];
        _advance = [.. all.OfType<IAdvanceMatch>()];
    }

    public IReadOnlyList<IMatchService> All { get; }

    public void RosterChanged()
    {
        foreach (IObserveMatchRoster service in _rosterObservers)
        {
            service.RosterChanged();
        }
    }

    public void Enter(Player player, int generation, bool initialPhase)
    {
        foreach (IEnterMatch service in _entrants)
        {
            service.Enter(player, generation, initialPhase);
        }
    }

    public void InputReceived(int payloadBytes)
    {
        foreach (IObserveMatchInput service in _inputObservers)
        {
            service.InputReceived(payloadBytes);
        }
    }

    public void MatchUpdated(in MatchUpdate update, ServerTime time)
    {
        foreach (IObserveMatchUpdate service in _updateObservers)
        {
            service.MatchUpdated(update, time);
        }
    }

    public PhaseRequest Advance(ServerTime time)
    {
        foreach (IAdvanceMatch service in _advance)
        {
            PhaseRequest request = service.Advance(time);
            if (request != PhaseRequest.NONE)
                return request;
        }

        return PhaseRequest.NONE;
    }

    public static MatchServices Open(MatchRuntime runtime, MapSnapshot map, MatchDependencies dependencies)
    {
        MatchReplication replication = new(
            runtime,
            dependencies.Roster,
            map,
            dependencies.Link,
            dependencies.Log,
            dependencies.NetStats);

        IMatchService[] services =
        [
            replication,
            new EmptyMatchTimeout(runtime, dependencies.Clock),
        ];
        return new MatchServices(services);
    }
}
