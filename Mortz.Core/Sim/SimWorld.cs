using Mortz.Core.Input;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Replication;
using Mortz.Core.Terrain;

namespace Mortz.Core.Sim;

/// <summary>
/// The authoritative game simulation: a fixed-tick, deterministic state machine.
/// The server owns the real one; clients run a predicted copy of their own
/// player. Don't touch engine APIs, wall-clock time or unordered collections
/// in here: the same inputs must always produce the same state.
/// </summary>
public sealed class SimWorld
{
    public enum MortarEventKind : byte { Spawn, Deflect, End }
    public readonly record struct MortarEvent(MortarEventKind Kind, MortarState State);

    public int Tick { get; private set; }
    public TerrainMask Terrain { get; }
    public MatchConfig Config { get; }

    // Sorted so iteration order (and thus any future interactions) is deterministic.
    private readonly SortedDictionary<int, PlayerState> _players = new();
    private readonly SortedDictionary<int, InputQueue> _inputs = new();
    // Resolved per player at join; perk selections feed in here eventually.
    private readonly SortedDictionary<int, PlayerStats> _stats = new();
    private readonly SortedDictionary<int, byte> _netSlots = new();
    private readonly Vec2[] _spawnPoints;

    // Shells in flight, in spawn order.
    private readonly List<MortarState> _mortars = new();
    private readonly List<MortarState> _forcedMortarExplosions = new();
    private readonly List<(int X, int Y, int Radius, int OwnerId, int SpawnSeq)> _explosions = new();
    private readonly List<(int FiredBy, int SpawnSeq)> _shellRetirements = new();
    private readonly List<MortarEvent> _mortarEvents = new();
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

    /// <summary>Predicted shells the server took over this Step. The server sends
    /// these reliably to the original shooter; snapshots remain only a fallback.</summary>
    public IReadOnlyList<(int FiredBy, int SpawnSeq)> ShellRetirements => _shellRetirements;

    /// <summary>Ordered authoritative shell lifecycle changes from the last
    /// Step. Reliable delivery replaces repeating complete shells in every
    /// player snapshot.</summary>
    public IReadOnlyList<MortarEvent> MortarEvents => _mortarEvents;

    /// <summary>Deaths from the last Step (blast or death pit), with the body
    /// center at the moment of death; the server broadcasts these for gibs and
    /// scores them. KillerId is the explosion's owner (a parried shell belongs
    /// to the parrier), 0 for a death pit; the victim's own id is a suicide.
    /// Owned = a parried shell killed the very player who fired it.</summary>
    public IReadOnlyList<(int PeerId, Vec2 Position, int KillerId, bool Owned)> Deaths => _deaths;

    public SimWorld(TerrainMask terrain, MatchConfig config, int seed = 0,
        IReadOnlyList<Vec2>? spawnPoints = null)
    {
        Terrain = terrain;
        Config = config;
        _rng = new Random(seed);
        _spawnPoints = spawnPoints?.ToArray() ?? [];
    }

    public void AddPlayer(int peerId, byte teamId = 0)
    {
        byte slot = Enumerable.Range(1, NetConfig.MAX_PLAYERS)
            .Select(i => (byte)i)
            .First(i => !_netSlots.ContainsValue(i));
        _netSlots[peerId] = slot;
        _stats[peerId] = PlayerStats.Resolve(Config);
        _players[peerId] = FreshState(peerId, lastInputSeq: -1) with
        {
            Skin = (byte)_rng.Next(SimConfig.SKIN_COUNT),
            TeamId = teamId,
        };
        _inputs[peerId] = new InputQueue();
    }

    private PlayerState FreshState(int peerId, int lastInputSeq)
    {
        PlayerStats stats = _stats[peerId];
        Vec2 spawn = FindSpawn(peerId);
        return new PlayerState
        {
            PeerId = peerId,
            NetSlot = _netSlots[peerId],
            Position = spawn,
            Grounded = PlayerSim.OnGround(Terrain, spawn),
            JumpsLeft = stats.TotalJumps,
            Ammo = stats.MaxAmmo,
            Health = stats.MaxHealth,
            SpawnImmunityTicks = (byte)Config.SpawnImmunityTicks,
            SpawnImmunityFireThroughSeq = lastInputSeq + Config.SpawnImmunityTicks,
            LastInputSeq = lastInputSeq,
        };
    }

    /// <summary>
    /// Authored spawn points, handed out by net slot. Maps without any fall
    /// back to the old stable-per-peer column search.
    /// </summary>
    private Vec2 FindSpawn(int peerId)
    {
        if (_spawnPoints.Length > 0)
            return _spawnPoints[(_netSlots[peerId] - 1) % _spawnPoints.Length];

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
        _netSlots.Remove(peerId);
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
        _shellRetirements.Clear();
        _mortarEvents.Clear();
        _forcedMortarExplosions.Clear();
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
                    state = FreshState(id, queue.LastAppliedSeq) with
                    {
                        Skin = prev.Skin,
                        TeamId = prev.TeamId,
                    };
            }
            else
            {
                PlayerStats stats = _stats[id];
                // The effective input may contain carried actions. Force any raw
                // press consumed by the drain to be an edge against the simulated
                // state, then restore the raw applied buttons as the next replay
                // anchor; carried buttons are one-tick actions, not held state.
                PlayerState simPrev = prev with
                {
                    PrevButtons = prev.PrevButtons & ~queue.PressedButtons,
                };
                state = PlayerSim.Tick(simPrev, input, Terrain, stats);
                // Shells are world entities and this is the authoritative sim, so
                // the spawn happens here; prediction runs the same WeaponSim but
                // keeps its shells cosmetic. Run the weapon per consumed input, not
                // just the applied one, so a fire the drain overtook still fires
                // with its own aim and seq, and reload advances a step per input.
                InputButtons prevButtons = prev.PrevButtons;
                foreach ((int seq, PlayerInput consumed) in queue.Consumed)
                {
                    if (WeaponSim.Tick(ref state, consumed, prevButtons, stats, seq))
                        SpawnMortar(WeaponSim.NewShell(_nextMortarId++, seq, state, consumed, Config));
                    prevButtons = consumed.Buttons;
                }
                state.PrevButtons = queue.RawAppliedInput.Buttons;
                state.Aim = queue.RawAppliedInput.Aim;
                if (FellOutOfTheMap(state))
                {
                    _deaths.Add((id, BodyCenter(state), 0, false)); // death pit: no killer
                    state = Corpse(state);
                }
            }
            state.LastInputSeq = queue.LastAppliedSeq;
            _players[id] = state;
        }
        foreach (MortarState forced in _forcedMortarExplosions)
            Explode(forced);
        _forcedMortarExplosions.Clear();
        StepMortars();
        Tick++;
    }

    private void SpawnMortar(MortarState mortar)
    {
        if (_mortars.Count >= SimConfig.MAX_ACTIVE_MORTARS)
        {
            MortarState retired = _mortars[0];
            _mortars.RemoveAt(0);
            _forcedMortarExplosions.Add(retired);
            _mortarEvents.Add(new MortarEvent(MortarEventKind.End, retired));
        }
        _mortars.Add(mortar);
        _mortarEvents.Add(new MortarEvent(MortarEventKind.Spawn, mortar));
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
            _mortarEvents.Add(new MortarEvent(MortarEventKind.End, m));
        }
    }

    /// <summary>
    /// An active parry bubble flips an approaching shell straight back along
    /// its trajectory and refunds the parrier's cooldown. The parrier takes
    /// ownership (their kill now), FiredBy remembers who shot it for the OWNED
    /// check, and SpawnSeq is kept so the original shooter can recognise its
    /// deflected shell in a snapshot and retire the predicted copy that would
    /// otherwise keep flying straight. The eventual carve still matches nobody:
    /// Explode broadcasts -1 for a deflected shell. The approach test doubles
    /// as the re-deflect guard: once flipped, the shell is receding.
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
            if (!m.Deflected)
                _shellRetirements.Add((m.FiredBy, m.SpawnSeq));
            m.OwnerId = id;
            m.Deflected = true;
            _mortarEvents.Add(new MortarEvent(MortarEventKind.Deflect, m));
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
        // A deflected shell keeps the shooter's seq for snapshot retirement, but
        // its carve belongs to no prediction: broadcast -1 so the new owner can't
        // match it.
        int carveSeq = m.Deflected ? -1 : m.SpawnSeq;
        _explosions.Add(((int)at.X, (int)at.Y, Config.MortarCarveRadius, m.OwnerId, carveSeq));

        foreach (int id in _players.Keys.ToArray())
        {
            PlayerState p = _players[id];
            if (!CombatEligibility.CanTakeDamage(p))
                continue; // already gibbed or protected, nothing to hurt
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
        SpawnImmunityTicks = 0,
    };

    private static Vec2 BodyCenter(in PlayerState p) =>
        p.Position with { Y = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };

    /// <summary>Body entirely below the bottom edge plus a grace depth, so the
    /// fall reads as a fall. Side/top exits aren't lethal on their own; gravity
    /// brings them down here anyway.</summary>
    private bool FellOutOfTheMap(in PlayerState p) =>
        p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 > Terrain.Height + SimConfig.DEATH_PIT_DEPTH;

    public Snapshot TakeSnapshot(bool includeMortars = true) =>
        new(Tick, _players.Values.ToArray(), includeMortars ? _mortars.ToArray() : []);
}
