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

    // Shells in flight, in spawn order.
    private readonly List<MortarState> _mortars = new();
    private readonly List<(int X, int Y, int Radius, int OwnerId, int SpawnSeq)> _explosions = new();
    private readonly List<(int PeerId, Vec2 Position)> _deaths = new();
    private ushort _nextMortarId;

    // Only ever drawn from at AddPlayer; a fixed seed keeps tests reproducible,
    // the server passes a random one.
    private readonly Random _rng;

    public IReadOnlyDictionary<int, PlayerState> Players => _players;
    public IReadOnlyList<MortarState> Mortars => _mortars;

    /// <summary>Terrain impacts from the last Step; the server broadcasts these
    /// as carves. Owner and firing seq let the owner's client match its
    /// predicted carve to the authoritative one.</summary>
    public IReadOnlyList<(int X, int Y, int Radius, int OwnerId, int SpawnSeq)> Explosions => _explosions;

    /// <summary>Deaths from the last Step (blast or death pit), with the body
    /// center at the moment of death; the server broadcasts these for gibs.</summary>
    public IReadOnlyList<(int PeerId, Vec2 Position)> Deaths => _deaths;

    public SimWorld(TerrainMask terrain, int seed = 0)
    {
        Terrain = terrain;
        _rng = new Random(seed);
    }

    public void AddPlayer(int peerId)
    {
        _players[peerId] = FreshState(peerId) with { Skin = (byte)_rng.Next(SimConfig.SKIN_COUNT) };
        _inputs[peerId] = new InputQueue();
    }

    private PlayerState FreshState(int peerId) => new()
    {
        PeerId = peerId,
        Position = FindSpawn(peerId),
        Grounded = true,
        JumpsLeft = SimConfig.TOTAL_JUMPS,
        Ammo = SimConfig.MORTAR_MAX_AMMO,
        Health = SimConfig.MAX_HEALTH,
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

    /// <summary>Diagnostics: input backlog for one player (ticks of standing latency).</summary>
    public int PendingInputs(int peerId) =>
        _inputs.TryGetValue(peerId, out InputQueue? queue) ? queue.PendingCount : 0;

    public void Step()
    {
        _explosions.Clear();
        _deaths.Clear();
        foreach (int id in _players.Keys.ToArray())
        {
            InputQueue queue = _inputs[id];
            PlayerInput input = queue.Next(); // consumed even by the dead: acks must keep flowing
            PlayerState prev = _players[id];
            PlayerState state;
            if (prev.RespawnTicks > 0)
            {
                state = prev;
                if (--state.RespawnTicks == 0)
                    state = FreshState(id) with { Skin = prev.Skin };
            }
            else
            {
                state = PlayerSim.Tick(prev, input, Terrain);
                // Shells are world entities and this is the authoritative sim, so
                // the spawn happens here; prediction runs the same WeaponSim but
                // keeps its shells cosmetic.
                if (WeaponSim.Tick(ref state, input, prev.PrevButtons))
                    _mortars.Add(WeaponSim.NewShell(_nextMortarId++, queue.LastAppliedSeq, state, input));
                if (FellOutOfTheMap(state))
                {
                    // Death pit (a scored death once deathmatch exists).
                    _deaths.Add((id, BodyCenter(state)));
                    state = Corpse(state);
                }
            }
            state.LastInputSeq = queue.LastAppliedSeq;
            _players[id] = state;
        }
        StepMortars();
        Tick++;
    }

    private void StepMortars()
    {
        for (int i = _mortars.Count - 1; i >= 0; i--)
        {
            MortarState m = _mortars[i];
            MortarOutcome outcome = MortarSim.Tick(ref m, Terrain, SimConfig.DT);
            if (outcome == MortarOutcome.Flying && DirectHit(m))
                outcome = MortarOutcome.Exploded;
            if (outcome == MortarOutcome.Flying)
            {
                _mortars[i] = m;
                continue;
            }
            if (outcome == MortarOutcome.Exploded)
                Explode(m);
            _mortars.RemoveAt(i);
        }
    }

    /// <summary>
    /// A shell touching someone else's body detonates on them. The shooter is
    /// immune to contact (the muzzle would pop diagonal shots at spawn), but
    /// not to the blast. Checked once per tick: at 15 px/tick against a 32 px
    /// body a shell can't pass through anyone between checks.
    /// </summary>
    private bool DirectHit(in MortarState m)
    {
        foreach ((int id, PlayerState p) in _players)
        {
            if (id == m.OwnerId || p.RespawnTicks > 0)
                continue;
            if (m.Position.X >= p.Position.X - SimConfig.PLAYER_HALF_WIDTH &&
                m.Position.X < p.Position.X + SimConfig.PLAYER_HALF_WIDTH &&
                m.Position.Y >= p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 &&
                m.Position.Y < p.Position.Y)
                return true;
        }
        return false;
    }

    /// <summary>Carve the hole, then hurt everyone in the blast circle with
    /// BlastSim's falloff, shooter included: point blank is still suicide.
    /// Running out of health is a death like any other. The explosion is
    /// reported even when the carve removes nothing (all Solid).</summary>
    private void Explode(in MortarState m)
    {
        Vec2 at = m.Position;
        Terrain.CarveCircle((int)at.X, (int)at.Y, SimConfig.MORTAR_CARVE_RADIUS);
        _explosions.Add(((int)at.X, (int)at.Y, SimConfig.MORTAR_CARVE_RADIUS, m.OwnerId, m.SpawnSeq));

        foreach (int id in _players.Keys.ToArray())
        {
            PlayerState p = _players[id];
            if (p.RespawnTicks > 0)
                continue; // already gibbed, nothing left to hurt
            int damage = BlastSim.Damage(p, at);
            if (damage == 0)
                continue;
            if (damage >= p.Health)
            {
                _deaths.Add((id, BodyCenter(p)));
                _players[id] = Corpse(p);
                continue;
            }
            p.Health = (byte)(p.Health - damage);
            _players[id] = p;
        }
    }

    /// <summary>The body stays where it died (hidden by the view, gibs mark the
    /// spot) until Step's countdown respawns it. Rope drops so nothing renders.</summary>
    private static PlayerState Corpse(in PlayerState p) => p with
    {
        Velocity = Vec2.Zero,
        Health = 0,
        Rope = RopeMode.None,
        RespawnTicks = (byte)SimConfig.RESPAWN_DELAY_TICKS,
    };

    private static Vec2 BodyCenter(in PlayerState p) =>
        p.Position with { Y = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };

    /// <summary>Body entirely below the bottom edge. Side/top exits aren't lethal
    /// on their own; gravity brings them down here anyway.</summary>
    private bool FellOutOfTheMap(in PlayerState p) =>
        p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 > Terrain.Height;

    public Snapshot TakeSnapshot() => new(Tick, _players.Values.ToArray(), _mortars.ToArray());
}
