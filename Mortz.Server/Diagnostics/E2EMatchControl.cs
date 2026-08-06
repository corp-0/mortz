using Mortz.Core.Sim;

namespace Mortz.Server.Diagnostics;

/// <summary>Queues mutations and state reads for tick boundaries; enqueue from
/// the main thread only, the driver already marshals. A mutation lands before
/// Step and completes after, so AppliedTick is the first tick whose snapshot
/// and deaths include it.</summary>
public sealed class E2EMatchControl : IMatchControl
{
    private readonly List<PlacementRequest> _placements = [];
    private readonly List<DamageRequest> _damages = [];
    private readonly List<Action<WorldStateOutcome>> _reads = [];
    private readonly List<Action<MatchSetupOutcome>> _setupReads = [];

    private readonly List<PlacementRequest> _placementsInFlight = [];
    private readonly List<DamageRequest> _damagesInFlight = [];
    private readonly List<Action<WorldStateOutcome>> _readsInFlight = [];
    private readonly List<Action<MatchSetupOutcome>> _setupReadsInFlight = [];

    public void PlacePlayer(int peerId, Vec2 position, Action<MutationOutcome> done) =>
        _placements.Add(new PlacementRequest(peerId, position, done));

    public void DamagePlayer(int peerId, int amount, Action<DamageOutcome> done) =>
        _damages.Add(new DamageRequest(peerId, amount, done));

    public void ReadState(Action<WorldStateOutcome> done) => _reads.Add(done);

    /// <summary>Rules and arena. Constant for the match, but served on the same
    /// queue so there is one way in and no second view of the world.</summary>
    public void ReadSetup(Action<MatchSetupOutcome> done) => _setupReads.Add(done);

    public void ApplyBefore(SimWorld world)
    {
        foreach (PlacementRequest request in _placements)
        {
            _placementsInFlight.Add(request with { Error = Place(world, request) });
        }
        _placements.Clear();

        foreach (DamageRequest request in _damages)
        {
            if (!world.Players.ContainsKey(request.PeerId))
            {
                _damagesInFlight.Add(request with { Error = Unknown(request.PeerId) });
                continue;
            }
            world.QueueDamage(request.PeerId, request.Amount);
            _damagesInFlight.Add(request);
        }
        _damages.Clear();

        _readsInFlight.AddRange(_reads);
        _reads.Clear();

        _setupReadsInFlight.AddRange(_setupReads);
        _setupReads.Clear();
    }

    public void CompleteAfter(SimWorld world)
    {
        foreach (PlacementRequest request in _placementsInFlight)
        {
            request.Done(new MutationOutcome(world.Tick, request.Error));
        }
        _placementsInFlight.Clear();

        foreach (DamageRequest request in _damagesInFlight)
        {
            request.Done(Settle(world, request));
        }
        _damagesInFlight.Clear();

        if (_setupReadsInFlight.Count > 0)
        {
            MatchSetupOutcome setup = new(
                world.Config.ToBytes(), world.Terrain.Width, world.Terrain.Height);
            foreach (Action<MatchSetupOutcome> done in _setupReadsInFlight)
            {
                done(setup);
            }
            _setupReadsInFlight.Clear();
        }

        if (_readsInFlight.Count == 0)
            return;
        WorldStateOutcome state = Describe(world);
        foreach (Action<WorldStateOutcome> done in _readsInFlight)
        {
            done(state);
        }
        _readsInFlight.Clear();
    }

    /// <summary>Null when the teleport happened; a message when it could not.
    /// A missing or dead target is reported, never thrown into the tick.</summary>
    private static string? Place(SimWorld world, in PlacementRequest request)
    {
        if (!world.Players.TryGetValue(request.PeerId, out PlayerState player))
            return Unknown(request.PeerId);
        if (player.RespawnTicks > 0)
            return $"Player {request.PeerId} is dead.";
        world.Teleport(request.PeerId, request.Position);
        return null;
    }

    private static DamageOutcome Settle(SimWorld world, in DamageRequest request)
    {
        if (request.Error != null)
            return new DamageOutcome(world.Tick, 0, false, request.Error);
        if (!world.Players.TryGetValue(request.PeerId, out PlayerState player))
            return new DamageOutcome(world.Tick, 0, false, Unknown(request.PeerId));
        return new DamageOutcome(world.Tick, player.Health, player.Health == 0, null);
    }

    private static WorldStateOutcome Describe(SimWorld world) => new(
        world.Tick,
        world.Players.Values
            .Select(player => new E2EWorldPlayer(
                player.PeerId, player.Position, player.Velocity, player.Health,
                player.RespawnTicks))
            .ToArray());

    private static string Unknown(int peerId) => $"Unknown peer {peerId}.";
}
