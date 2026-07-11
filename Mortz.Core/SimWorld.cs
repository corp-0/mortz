namespace Mortz.Core;

/// <summary>
/// The authoritative game simulation: a fixed-tick, deterministic state machine.
/// The server owns the real one; clients run a predicted copy of their own
/// player. Don't touch engine APIs, wall-clock time or unordered collections
/// in here: the same inputs must always produce the same state.
/// </summary>
public sealed class SimWorld
{
    public int Tick { get; private set; }
    public TerrainMask Terrain { get; }

    // Sorted so iteration order (and thus any future interactions) is deterministic.
    private readonly SortedDictionary<int, PlayerState> _players = new();
    private readonly SortedDictionary<int, InputQueue> _inputs = new();

    public IReadOnlyDictionary<int, PlayerState> Players => _players;

    public SimWorld(TerrainMask terrain)
    {
        Terrain = terrain;
    }

    public void AddPlayer(int peerId)
    {
        _players[peerId] = FreshState(peerId);
        _inputs[peerId] = new InputQueue();
    }

    private PlayerState FreshState(int peerId) => new()
    {
        PeerId = peerId,
        Position = FindSpawn(peerId),
        Grounded = true,
        JumpsLeft = SimConfig.TOTAL_JUMPS,
        LastInputSeq = -1,
    };

    /// <summary>
    /// Stable-per-peer spawn x, then the highest standing spot in that column.
    /// Manifest-defined spawn points replace this eventually.
    /// </summary>
    private Vec2 FindSpawn(int peerId)
    {
        // Long math: ENet peer ids are large random ints, int multiply overflows.
        int margin = (int)SimConfig.PLAYER_HALF_WIDTH * 3;
        float x = margin + (int)(Math.Abs((long)peerId * 193) % (Terrain.Width - 2 * margin));
        for (int y = (int)SimConfig.PLAYER_HALF_HEIGHT * 2 + 1; y < Terrain.Height; y++)
        {
            Vec2 feet = new Vec2(x, y);
            if (!PlayerSim.BodyBlocked(Terrain, feet) && PlayerSim.OnGround(Terrain, feet))
                return feet;
        }
        return new Vec2(x, Terrain.Height / 2f); // no floor in this column: drop them mid-air
    }

    public void RemovePlayer(int peerId)
    {
        _players.Remove(peerId);
        _inputs.Remove(peerId);
    }

    public void EnqueueInput(int peerId, int seq, PlayerInput input)
    {
        if (_inputs.TryGetValue(peerId, out InputQueue? queue))
            queue.Enqueue(seq, input);
    }

    public void Step()
    {
        foreach (int id in _players.Keys.ToArray())
        {
            InputQueue queue = _inputs[id];
            PlayerState state = PlayerSim.Tick(_players[id], queue.Next(), Terrain);
            if (FellOutOfTheMap(state))
                state = FreshState(id); // death pit: respawn (a scored death once deathmatch exists)
            state.LastInputSeq = queue.LastAppliedSeq;
            _players[id] = state;
        }
        Tick++;
    }

    /// <summary>Body entirely below the bottom edge. Side/top exits aren't lethal
    /// on their own; gravity brings them down here anyway.</summary>
    private bool FellOutOfTheMap(in PlayerState p) =>
        p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 > Terrain.Height;

    public Snapshot TakeSnapshot() => new(Tick, _players.Values.ToArray());
}
