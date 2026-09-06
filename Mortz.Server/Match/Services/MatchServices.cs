using Mortz.Core.Features;
using Mortz.Server.Content;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Services;

/// <summary>The services whose lifetime is exactly one match. Services sharing a
/// lifecycle interface run in registration order.</summary>
public class MatchServices : IDisposable
{
    public FeatureScope Scope { get; } = new();
    private readonly TerrainHistoryService _terrain;

    private MatchServices(IReadOnlyList<IMatchService> all, TerrainHistoryService terrain)
    {
        _terrain = terrain;
        foreach (IMatchService feature in all)
        {
            Scope.Register(feature);
        }
        Scope.Start();
        All = Scope.Implementing<IMatchService>();
    }

    public void Dispose() => Scope.Dispose();

    public IReadOnlyList<IMatchService> All { get; }

    public void RosterChanged()
    {
        foreach (IObserveMatchRoster service in Scope.Implementing<IObserveMatchRoster>())
        {
            service.RosterChanged();
        }
    }

    public void Enter(Player player, int generation, bool initialPhase)
    {
        foreach (IEnterMatch service in Scope.Implementing<IEnterMatch>())
        {
            service.Enter(player, generation, initialPhase);
        }
    }

    public void InputReceived(int payloadBytes)
    {
        foreach (IObserveMatchInput service in Scope.Implementing<IObserveMatchInput>())
        {
            service.InputReceived(payloadBytes);
        }
    }

    public void MatchUpdated(in MatchUpdate update, ServerTime time)
    {
        _terrain.MatchUpdated(update, time);
        foreach (IObserveMatchUpdate service in Scope.Implementing<IObserveMatchUpdate>())
        {
            service.MatchUpdated(update, time);
        }
    }

    public PhaseRequest Advance(ServerTime time)
    {
        foreach (IAdvanceMatch service in Scope.Implementing<IAdvanceMatch>())
        {
            PhaseRequest request = service.Advance(time);
            if (request != PhaseRequest.NONE)
                return request;
        }

        return PhaseRequest.NONE;
    }

    public static MatchServices Open(MatchRuntime runtime, MapSnapshot map, MatchDependencies dependencies)
    {
        TerrainHistory terrainHistory = new();
        MatchReplication replication = new(
            runtime,
            dependencies.Roster,
            map,
            terrainHistory,
            dependencies.Link,
            dependencies.Log,
            dependencies.NetStats);

        IMatchService[] services =
        [
            replication,
            new MatchPointService(dependencies.Link, dependencies.Log),
            new MatchWinRecorder(runtime, dependencies.Wins),
            new MatchUpdateObserver(dependencies.Observer),
            new EmptyMatchTimeout(runtime, dependencies.Clock),
        ];
        return new MatchServices(services, new TerrainHistoryService(terrainHistory));
    }
}
