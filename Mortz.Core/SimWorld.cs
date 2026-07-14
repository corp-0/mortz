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
    public MatchConfig Config { get; }

    // Sorted so iteration order (and thus any future interactions) is deterministic.
    private readonly SortedDictionary<int, PlayerState> _players = new();
    private readonly SortedDictionary<int, InputQueue> _inputs = new();
    // Resolved per player at join; perk selections feed in here eventually.
    private readonly SortedDictionary<int, PlayerStats> _stats = new();

    // Shells in flight, in spawn order.
    private readonly List<MortarState> _mortars = new();
    private readonly List<(int X, int Y, int Radius, int OwnerId, int SpawnSeq)> _explosions = new();
    private readonly List<(int PeerId, Vec2 Position, int KillerId, bool Owned)> _deaths = new();
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
    /// center at the moment of death; the server broadcasts these for gibs and
    /// scores them. KillerId is the explosion's owner (a parried shell belongs
    /// to the parrier), 0 for a death pit; the victim's own id is a suicide.
    /// Owned = a parried shell killed the very player who fired it.</summary>
    public IReadOnlyList<(int PeerId, Vec2 Position, int KillerId, bool Owned)> Deaths => _deaths;

    public SimWorld(TerrainMask terrain, MatchConfig config, int seed = 0)
    {
        Terrain = terrain;
        Config = config;
        _rng = new Random(seed);
    }

    public void AddPlayer(int peerId, byte teamId = 0)
    {
        _stats[peerId] = PlayerStats.Resolve(Config);
        _players[peerId] = FreshState(peerId) with
        {
            Skin = (byte)_rng.Next(SimConfig.SKIN_COUNT),
            TeamId = teamId,
        };
        _inputs[peerId] = new InputQueue();
    }

    private PlayerState FreshState(int peerId)
    {
        PlayerStats stats = _stats[peerId];
        return new PlayerState
        {
            PeerId = peerId,
            Position = FindSpawn(peerId),
            Grounded = true,
            JumpsLeft = stats.TotalJumps,
            Ammo = stats.MaxAmmo,
            Health = stats.MaxHealth,
            LastInputSeq = -1,
        };
    }

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
        _stats.Remove(peerId);
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
                    state = FreshState(id) with { Skin = prev.Skin, TeamId = prev.TeamId };
            }
            else
            {
                PlayerStats stats = _stats[id];
                state = PlayerSim.Tick(prev, input, Terrain, stats);
                // Shells are world entities and this is the authoritative sim, so
                // the spawn happens here; prediction runs the same WeaponSim but
                // keeps its shells cosmetic.
                // FireSeq, not LastAppliedSeq: a press overtaken by the
                // backlog drain must still match the client's predicted shell.
                if (WeaponSim.Tick(ref state, input, prev.PrevButtons, stats))
                    _mortars.Add(WeaponSim.NewShell(_nextMortarId++, queue.FireSeq, state, input, Config));
                if (FellOutOfTheMap(state))
                {
                    _deaths.Add((id, BodyCenter(state), 0, false)); // death pit: no killer
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
            MortarOutcome outcome = MortarSim.Tick(ref m, Terrain, Config, SimConfig.DT);
            if (outcome == MortarOutcome.Flying)
                TryDeflect(ref m);
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
    /// An active parry bubble flips an approaching shell straight back along
    /// its trajectory and refunds the parrier's cooldown. The parrier takes
    /// ownership (their kill now), FiredBy remembers who shot it for the OWNED
    /// check, and SpawnSeq goes to -1 so the eventual carve matches nobody's
    /// predicted one. The shooter's own predicted copy keeps flying straight
    /// and its carve reverts on the ledger timeout, same artifact as a direct
    /// hit today. The approach test doubles as the re-deflect guard: once
    /// flipped, the shell is receding.
    /// </summary>
    private void TryDeflect(ref MortarState m)
    {
        foreach ((int id, PlayerState p) in _players)
        {
            if (p.ParryTicks == 0 || p.RespawnTicks > 0)
                continue;
            Vec2 toCenter = BodyCenter(p) - m.Position;
            float radius = _stats[id].ParryRadius;
            if (toCenter.LengthSquared() > radius * radius || Vec2.Dot(m.Velocity, toCenter) <= 0)
                continue;
            m.Velocity = -m.Velocity;
            m.OwnerId = id;
            m.Deflected = true;
            m.SpawnSeq = -1;
            _players[id] = p with { ParryCooldown = 0 };
            return;
        }
    }

    /// <summary>
    /// A shell touching someone else's body detonates on them. The shooter is
    /// immune to contact (the muzzle would pop diagonal shots at spawn), but
    /// not to the blast. A raised parry bubble keeps shells off the body: they
    /// deflect before they can ever reach it. Checked once per tick: at 15
    /// px/tick against a 32 px body a shell can't pass through anyone between
    /// checks.
    /// </summary>
    private bool DirectHit(in MortarState m)
    {
        foreach ((int id, PlayerState p) in _players)
        {
            if (id == m.OwnerId || p.RespawnTicks > 0 || p.ParryTicks > 0)
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
        Terrain.CarveCircle((int)at.X, (int)at.Y, Config.MortarCarveRadius);
        _explosions.Add(((int)at.X, (int)at.Y, Config.MortarCarveRadius, m.OwnerId, m.SpawnSeq));

        foreach (int id in _players.Keys.ToArray())
        {
            PlayerState p = _players[id];
            if (p.RespawnTicks > 0)
                continue; // already gibbed, nothing left to hurt
            int damage = BlastSim.Damage(p, at, Config);
            if (damage == 0 || SparedByFriendlyFire(p, m.OwnerId))
                continue;
            if (damage >= p.Health)
            {
                // OWNED: the parried shell came back for its own shooter.
                _deaths.Add((id, BodyCenter(p), m.OwnerId, m.Deflected && id == m.FiredBy));
                _players[id] = Corpse(p);
                continue;
            }
            p.Health = (byte)(p.Health - damage);
            _players[id] = p;
        }
    }

    /// <summary>Friendly fire off spares teammates from blast damage; shells
    /// still explode and carve. The shooter is never spared: point blank stays
    /// suicide. A parried shell belongs to the parrier, so it hurts the
    /// original shooter regardless of teams.</summary>
    private bool SparedByFriendlyFire(in PlayerState victim, int shooterId) =>
        !Config.FriendlyFire && Config.Teams &&
        victim.PeerId != shooterId && victim.TeamId != 0 &&
        _players.TryGetValue(shooterId, out PlayerState shooter) &&
        shooter.TeamId == victim.TeamId;

    /// <summary>The body stays where it died (hidden by the view, gibs mark the
    /// spot) until Step's countdown respawns it. Rope drops so nothing renders.</summary>
    private PlayerState Corpse(in PlayerState p) => p with
    {
        Velocity = Vec2.Zero,
        Health = 0,
        Rope = RopeMode.None,
        RespawnTicks = (byte)Config.RespawnDelayTicks,
    };

    private static Vec2 BodyCenter(in PlayerState p) =>
        p.Position with { Y = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };

    /// <summary>Body entirely below the bottom edge plus a grace depth, so the
    /// fall reads as a fall. Side/top exits aren't lethal on their own; gravity
    /// brings them down here anyway.</summary>
    private bool FellOutOfTheMap(in PlayerState p) =>
        p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 > Terrain.Height + SimConfig.DEATH_PIT_DEPTH;

    public Snapshot TakeSnapshot() => new(Tick, _players.Values.ToArray(), _mortars.ToArray());
}
