using Mortz.Core.Input;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Replication;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;

namespace Mortz.Core.Sim;

/// <summary>
/// The authoritative fixed-tick sim. No engine APIs, wall-clock time or
/// unordered collections in here: the same inputs must always produce the
/// same state.
/// </summary>
public sealed class SimWorld(
    TerrainMask terrain,
    MatchConfig config,
    IReadOnlyList<SpawnPoint>? spawnPoints,
    MapZones? zones = null)
{
    public enum MortarEventKind : byte
    {
        SPAWN,
        DEFLECT,
        END,
    }

    public readonly record struct MortarEvent(MortarEventKind Kind, MortarState State);

    public int Tick { get; private set; }
    public TerrainMask Terrain { get; } = terrain;
    public MatchConfig Config { get; } = config;
    public MapZones Zones { get; } = zones ?? MapZones.None;

    // Sorted for deterministic iteration order.
    private readonly SortedDictionary<int, PlayerState> _players = new();
    private readonly SortedDictionary<int, InputQueue> _inputs = new();
    // _stats replicates (config + persistent modifiers); situations compose
    // on top into _effective, which is what the sim reads.
    private readonly SortedDictionary<int, PlayerStats> _stats = new();
    private readonly SortedDictionary<int, List<StatsModifier>> _modifiers = new();
    private readonly SortedDictionary<int, Situations> _situations = new();
    private readonly SortedDictionary<int, ulong> _zoneMasks = new();
    private readonly SortedDictionary<int, PlayerStats> _effective = new();
    private readonly SortedDictionary<int, byte> _netSlots = new();
    private readonly SpawnPoint[] _spawnPoints = spawnPoints?.ToArray() ?? [];
    private readonly Dictionary<int, int> _spawnAssignments = [];

    // Shells in flight, in spawn order.
    private readonly List<MortarState> _mortars = new();
    private readonly List<MortarState> _forcedMortarExplosions = new();
    private readonly List<Explosion> _explosions = new();
    private readonly List<ShellRetirement> _shellRetirements = new();
    private readonly List<MortarEvent> _mortarEvents = new();
    private readonly List<Death> _deaths = new();
    private readonly List<PendingDamage> _pendingDamage = new();
    private ushort _nextMortarId;

    public IReadOnlyDictionary<int, PlayerState> Players => _players;
    public IReadOnlyDictionary<int, PlayerStats> Stats => _stats;
    public IReadOnlyList<MortarState> Mortars => _mortars;

    /// <summary>Terrain impacts from the last Step.</summary>
    public IReadOnlyList<Explosion> Explosions => _explosions;

    /// <summary>Predicted shells the server took over this Step.</summary>
    public IReadOnlyList<ShellRetirement> ShellRetirements => _shellRetirements;

    /// <summary>Ordered authoritative shell lifecycle changes from the last Step.</summary>
    public IReadOnlyList<MortarEvent> MortarEvents => _mortarEvents;

    /// <summary>Deaths from the last Step.</summary>
    public IReadOnlyList<Death> Deaths => _deaths;

    public SimWorld(TerrainMask terrain, MatchConfig config,
        IReadOnlyList<Vec2>? spawnPoints = null, MapZones? zones = null)
        : this(terrain, config, spawnPoints?.Select(point => new SpawnPoint(point)).ToArray(), zones)
    {
    }

    public void AddPlayer(int peerId, Team? team = null, byte skin = 0)
    {
        if (team != null && !Config.Rules.Teams)
            throw new ArgumentException("Team assignment with the Teams rule off.", nameof(team));
        if (skin >= SimConfig.SKIN_COUNT)
            throw new ArgumentOutOfRangeException(nameof(skin));
        byte slot = Enumerable.Range(1, NetConfig.MAX_PLAYERS)
            .Select(i => (byte)i)
            .First(i => !_netSlots.ContainsValue(i));
        _netSlots[peerId] = slot;
        _modifiers[peerId] = [];
        _situations[peerId] = Situations.NONE;
        _zoneMasks[peerId] = 0;
        _stats[peerId] = PlayerStats.Resolve(Config);
        _effective[peerId] = _stats[peerId];
        _players[peerId] = FreshState(peerId, team, lastInputSeq: -1) with
        {
            Skin = skin,
            Team = team,
        };
        _inputs[peerId] = new InputQueue();
    }

    /// <summary>Same id replaces; the stack stays sorted by id so composition is
    /// order-independent. The caller must rebroadcast PlayerModifiersMsg.</summary>
    public void AddModifier(int peerId, StatsModifier modifier)
    {
        if (!_modifiers.TryGetValue(peerId, out List<StatsModifier>? mods))
            return;
        mods.RemoveAll(m => m.Id == modifier.Id);
        int at = mods.FindIndex(m => m.Id > modifier.Id);
        mods.Insert(at < 0 ? mods.Count : at, modifier);
        RecomputeStats(peerId);
    }

    /// <summary>Recomputes from base without the id, never subtracts.</summary>
    public void RemoveModifier(int peerId, ModifierId id)
    {
        if (_modifiers.TryGetValue(peerId, out List<StatsModifier>? mods) &&
            mods.RemoveAll(m => m.Id == id) > 0)
            RecomputeStats(peerId);
    }

    public IReadOnlyList<StatsModifier> Modifiers(int peerId) => _modifiers[peerId];

    private void RecomputeStats(int peerId)
    {
        _stats[peerId] = StatsPipeline.Resolve(Config, _modifiers[peerId]);
        _effective[peerId] = Compose(peerId, _situations[peerId], _zoneMasks[peerId]);
    }

    /// <summary>Persistent (sorted by id), then situations (flag order), then
    /// zones (declaration order); the Predictor must compose identically.</summary>
    private PlayerStats Compose(int peerId, Situations flags, ulong zoneMask)
    {
        if (flags == Situations.NONE && zoneMask == 0)
            return _stats[peerId];
        List<StatsModifier> all = new(_modifiers[peerId]);
        SituationEffects.AppendModifiers(flags, all);
        SituationEffects.AppendZoneModifiers(zoneMask, Zones, all);
        return StatsPipeline.Resolve(Config, all);
    }

    /// <summary>Recomputes only when the situation flips, not every tick.</summary>
    private PlayerStats EffectiveStats(int id, in PlayerState state, in PlayerInput input)
    {
        Situations flags = SituationEffects.Detect(state, Terrain, input);
        ulong zoneMask = SituationEffects.DetectZones(state, Zones);
        if (flags != _situations[id] || zoneMask != _zoneMasks[id])
        {
            _situations[id] = flags;
            _zoneMasks[id] = zoneMask;
            _effective[id] = Compose(id, flags, zoneMask);
        }
        return _effective[id];
    }

    private PlayerState FreshState(int peerId, Team? team, int lastInputSeq)
    {
        PlayerStats stats = _stats[peerId];
        Vec2 spawn = FindSpawn(peerId, team);
        return new PlayerState
        {
            PeerId = peerId,
            NetSlot = _netSlots[peerId],
            Position = spawn,
            Grounded = PlayerSim.OnGround(Terrain, spawn),
            JumpsLeft = stats.TotalJumps,
            Ammo = stats.MaxAmmo,
            Health = stats.MaxHealth,
            SpawnImmunityTicks = (byte)Config.Rules.SpawnImmunityTicks,
            SpawnImmunityFireThroughSeq = lastInputSeq + Config.Rules.SpawnImmunityTicks,
            LastInputSeq = lastInputSeq,
        };
    }

    private Vec2 FindSpawn(int peerId, Team? team)
    {
        if (_spawnPoints.Length > 0)
        {
            SpawnPoint[] pool = SpawnPool(team);
            if (!_spawnAssignments.TryGetValue(peerId, out int assignment))
            {
                assignment = Config.Rules.Teams && team != null
                    ? _players.Values.Count(player => player.Team == team)
                    : _netSlots[peerId] - 1;
                _spawnAssignments[peerId] = assignment;
            }
            return pool[assignment % pool.Length].Position;
        }

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

    private SpawnPoint[] SpawnPool(Team? team)
    {
        if (!Config.Rules.Teams || team == null)
            return _spawnPoints;
        SpawnPoint[] owned = _spawnPoints.Where(point => point.Team == team).ToArray();
        if (owned.Length > 0)
            return owned;
        SpawnPoint[] neutral = _spawnPoints.Where(point => point.Team == null).ToArray();
        return neutral.Length > 0 ? neutral : _spawnPoints;
    }

    public void RemovePlayer(int peerId)
    {
        _players.Remove(peerId);
        _inputs.Remove(peerId);
        _stats.Remove(peerId);
        _modifiers.Remove(peerId);
        _situations.Remove(peerId);
        _zoneMasks.Remove(peerId);
        _effective.Remove(peerId);
        _netSlots.Remove(peerId);
        _spawnAssignments.Remove(peerId);
    }

    public void EnqueueInput(int peerId, int seq, PlayerInput input)
    {
        if (_inputs.TryGetValue(peerId, out InputQueue? queue))
            queue.Enqueue(seq, input);
    }

    /// <summary>
    /// Immediate, not queued: touches no per-tick output list, so calling this
    /// before a Step means that tick simulates from the new position. Player
    /// must already exist and be alive.
    /// </summary>
    public void Teleport(int peerId, Vec2 position)
    {
        PlayerState player = _players[peerId];
        _players[peerId] = player with
        {
            Position = position,
            Velocity = Vec2.Zero,
            Grounded = PlayerSim.OnGround(Terrain, position),
            DashCooldown = 0,
            Rope = RopeMode.NONE,
            RopePoint = Vec2.Zero,
            RopeVelocity = Vec2.Zero,
            RopeLength = 0,
        };
    }

    /// <summary>
    /// Queued, not immediate: Step clears the output lists first, so a Death
    /// added now would be wiped before it's ever scored. Runs inside the next
    /// Step through the same clamp-and-kill path as blast damage, spawn
    /// immunity included, and credits the kill to the victim (a suicide).
    /// </summary>
    public void QueueDamage(int peerId, int amount) =>
        _pendingDamage.Add(new PendingDamage(peerId, amount));

    /// <summary>Diagnostics: input backlog in ticks.</summary>
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
                    state = FreshState(id, prev.Team, queue.LastAppliedSeq) with
                    {
                        Skin = prev.Skin,
                        Team = prev.Team,
                    };
            }
            else
            {
                PlayerStats stats = EffectiveStats(id, prev, input);
                // Force presses the drain carried to read as edges, then restore
                // the raw applied buttons as the replay anchor: carried buttons
                // are one-tick actions, not held state.
                PlayerState simPrev = prev with
                {
                    PrevButtons = prev.PrevButtons.Except(queue.PressedButtons),
                };
                state = PlayerSim.Tick(simPrev, input, Terrain, stats);
                // Run the weapon per consumed input, not just the applied one: a
                // fire the drain overtook still fires with its own aim and seq,
                // and reload advances a step per input.
                InputButtons prevButtons = prev.PrevButtons;
                foreach ((int seq, PlayerInput consumed) in queue.Consumed)
                {
                    if (WeaponSim.Tick(ref state, consumed, prevButtons, stats, seq))
                        SpawnMortar(WeaponSim.NewShell(_nextMortarId++, seq, state, consumed, Config.Combat));
                    prevButtons = consumed.Buttons;
                }
                state.PrevButtons = queue.RawAppliedInput.Buttons;
                state.Aim = queue.RawAppliedInput.Aim;
                if (FellOutOfTheMap(state))
                {
                    _deaths.Add(new Death(id, state.BodyCenter,
                        KillerId: 0, Owned: false, ShellId: -1)); // death pit
                    state = Corpse(state);
                }
            }
            state.LastInputSeq = queue.LastAppliedSeq;
            _players[id] = state;
        }
        foreach (MortarState forced in _forcedMortarExplosions)
        {
            Explode(forced);
        }
        _forcedMortarExplosions.Clear();
        ApplyQueuedDamage();
        StepMortars();
        Tick++;
    }

    private void ApplyQueuedDamage()
    {
        foreach (PendingDamage pending in _pendingDamage)
        {
            if (pending.Amount <= 0 ||
                !_players.TryGetValue(pending.PeerId, out PlayerState player) ||
                !CombatEligibility.CanTakeDamage(player))
                continue;
            if (pending.Amount >= player.Health)
            {
                _deaths.Add(new Death(pending.PeerId, player.BodyCenter,
                    KillerId: pending.PeerId, Owned: false, ShellId: -1));
                _players[pending.PeerId] = Corpse(player);
                continue;
            }
            player.Health = (byte)(player.Health - pending.Amount);
            _players[pending.PeerId] = player;
        }
        _pendingDamage.Clear();
    }

    private void SpawnMortar(MortarState mortar)
    {
        if (_mortars.Count >= SimConfig.MAX_ACTIVE_MORTARS)
        {
            MortarState retired = _mortars[0];
            _mortars.RemoveAt(0);
            _forcedMortarExplosions.Add(retired);
            _mortarEvents.Add(new MortarEvent(MortarEventKind.END, retired));
        }
        _mortars.Add(mortar);
        _mortarEvents.Add(new MortarEvent(MortarEventKind.SPAWN, mortar));
    }

    private void StepMortars()
    {
        for (int i = _mortars.Count - 1; i >= 0; i--)
        {
            MortarState m = _mortars[i];
            MortarOutcome outcome = MortarSim.Tick(
                ref m, Terrain, Config.Combat, SimConfig.DT, Zones);
            if (outcome == MortarOutcome.FLYING)
                TryDeflect(ref m);
            if (outcome == MortarOutcome.FLYING && DirectHit(m))
                outcome = MortarOutcome.EXPLODED;
            if (outcome == MortarOutcome.FLYING)
            {
                _mortars[i] = m;
                continue;
            }
            if (outcome == MortarOutcome.EXPLODED)
                Explode(m);
            _mortars.RemoveAt(i);
            _mortarEvents.Add(new MortarEvent(MortarEventKind.END, m));
        }
    }

    /// <summary>
    /// A parry flips an approaching shell back and refunds the cooldown. The
    /// parrier takes ownership; FiredBy and SpawnSeq are kept so the OWNED
    /// check and the shooter's predicted-shell retirement still work. The
    /// approach test doubles as the re-deflect guard: a flipped shell is
    /// receding.
    /// </summary>
    private void TryDeflect(ref MortarState m)
    {
        foreach ((int id, PlayerState p) in _players)
        {
            if (p.ParryTicks == 0 || p.RespawnTicks > 0)
                continue;
            Vec2 toCenter = p.BodyCenter - m.Position;
            float radius = _effective[id].ParryRadius;
            if (toCenter.LengthSquared() > radius * radius || Vec2.Dot(m.Velocity, toCenter) <= 0)
                continue;
            m.Velocity = -m.Velocity;
            if (!m.Deflected)
                _shellRetirements.Add(new ShellRetirement(m.FiredBy, m.SpawnSeq));
            m.OwnerId = id;
            m.Deflected = true;
            _mortarEvents.Add(new MortarEvent(MortarEventKind.DEFLECT, m));
            _players[id] = p with { ParryCooldown = 0 };
            return;
        }
    }

    /// <summary>
    /// Contact detonation on someone else's body. The shooter is immune to
    /// contact (the muzzle would pop diagonal shots at spawn), not to the
    /// blast. Once per tick is enough: 15 px/tick can't tunnel a 32 px body.
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

    /// <summary>Carve, then damage everyone in the blast, shooter included:
    /// point blank is suicide. Reported even when the carve removes nothing.</summary>
    private void Explode(in MortarState m)
    {
        Vec2 at = m.Position;
        Terrain.CarveCircle((int)at.X, (int)at.Y, Config.Combat.MortarCarveRadius);
        // A deflected shell keeps the shooter's seq for retirement, but its
        // carve matches no prediction: broadcast -1.
        int carveSeq = m.Deflected ? -1 : m.SpawnSeq;
        _explosions.Add(new Explosion((int)at.X, (int)at.Y,
            Config.Combat.MortarCarveRadius, m.OwnerId, carveSeq));

        foreach (int id in _players.Keys.ToArray())
        {
            PlayerState p = _players[id];
            if (!CombatEligibility.CanTakeDamage(p))
                continue;
            if (!BlastSim.Reaches(p, at, Terrain))
                continue;
            int damage = BlastSim.Damage(p, at, Config.Combat);
            if (damage == 0 || SparedByFriendlyFire(p, m.OwnerId))
                continue;
            if (damage >= p.Health)
            {
                // OWNED: the parried shell came back for its own shooter.
                _deaths.Add(new Death(id, p.BodyCenter, m.OwnerId,
                    Owned: m.Deflected && id == m.FiredBy, ShellId: m.Id));
                _players[id] = Corpse(p);
                continue;
            }
            p.Health = (byte)(p.Health - damage);
            _players[id] = p;
        }
    }

    /// <summary>Only blast damage is spared; shells still explode and carve.</summary>
    private bool SparedByFriendlyFire(in PlayerState victim, int shooterId) =>
        !Config.Rules.FriendlyFire && victim.PeerId != shooterId &&
        _players.TryGetValue(shooterId, out PlayerState shooter) &&
        Teams.SameSide(victim.Team, shooter.Team);

    /// <summary>Body stays where it died until the respawn countdown ends;
    /// rope drops so nothing renders.</summary>
    private PlayerState Corpse(in PlayerState p) => p with
    {
        Velocity = Vec2.Zero,
        Health = 0,
        Rope = RopeMode.NONE,
        RespawnTicks = (ushort)Math.Max(1, Config.Rules.RespawnDelayTicks),
        SpawnImmunityTicks = 0,
    };

    /// <summary>Only the bottom edge kills; side/top exits fall back in.</summary>
    private bool FellOutOfTheMap(in PlayerState p) =>
        p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 > Terrain.Height + SimConfig.DEATH_PIT_DEPTH;

    public Snapshot TakeSnapshot(bool includeMortars = true) =>
        new(Tick, _players.Values.ToArray(), includeMortars ? _mortars.ToArray() : []);
}
