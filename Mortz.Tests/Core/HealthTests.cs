using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

/// <summary>
/// Blast damage through the full sim: grazes chip health, damage sticks (no
/// healing), and running out of health is a death like any other. Geometry:
/// the victim stands pinned at the left wall while the shooter suicide-fires
/// at their own feet from a spot whose blast reaches the victim's graze ring.
/// </summary>
public class HealthTests
{
    private const byte AIM_DOWN = 64;
    // Spawn column is 48 + |id*193| % 304 in the 400px world.
    private const int SHOOTER = 27; // x=91; 3 left taps drift to ~83
    private const int VICTIM = 30;  // x=62; walks left, pins at x=24
    private const int BACKUP = 4;   // x=212; safely outside the shooter's blasts

    [Fact]
    public void NewPlayer_SpawnsWithFullHealth()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        Assert.Equal(SimConfig.MAX_HEALTH, w.Players[1].Health);
    }

    [Fact]
    public void GrazeChipsHealth_DamageSticks_SecondGrazeKills_CorpseIsUntouchable()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(SHOOTER);
        w.AddPlayer(VICTIM);
        w.AddPlayer(BACKUP);
        int seq = 0;
        byte skin = w.Players[VICTIM].Skin;

        // Pin the victim to the left wall; it holds Left for the whole test.
        Step(w, ref seq, 60, InputButtons.None);
        Assert.InRange(w.Players[VICTIM].Position.X, 23, 26);

        WalkShooterIntoPlace(w, ref seq);
        PlayerState victimBefore = w.Players[VICTIM];

        // Shot 1: shooter suicides at own feet, victim catches the graze ring.
        Step(w, ref seq, 1, InputButtons.Fire);
        (int ex, int ey, _, _, _) = Assert.Single(w.Explosions);
        int expected = BlastSim.Damage(victimBefore, new Vec2(ex, ey), TestWorlds.NoSpawnProtectionConfig);
        Assert.InRange(expected, SimConfig.BLAST_EDGE_DAMAGE, SimConfig.MORTAR_DAMAGE - 1); // setup guard: a graze, not a core hit
        Assert.Equal([SHOOTER], w.Deaths.Select(d => d.PeerId)); // victim survived

        int health = w.Players[VICTIM].Health;
        Assert.InRange(health, SimConfig.MAX_HEALTH - expected - 2, SimConfig.MAX_HEALTH - expected + 2);

        // No healing while the shooter sits out their respawn delay.
        Step(w, ref seq, SimConfig.RESPAWN_DELAY_TICKS + 30, InputButtons.None);
        Assert.Equal(health, w.Players[VICTIM].Health);

        // Shot 2, same spot: the same graze now finishes the victim, who stays
        // a corpse instead of respawning on the spot.
        WalkShooterIntoPlace(w, ref seq);
        Step(w, ref seq, 1, InputButtons.Fire);
        Assert.Contains(VICTIM, w.Deaths.Select(d => d.PeerId));
        Assert.Equal(0, w.Players[VICTIM].Health);
        Assert.Equal(SimConfig.RESPAWN_DELAY_TICKS, w.Players[VICTIM].RespawnTicks);

        // Inside the dead window the backup walks up and suicide-blasts right
        // next to the corpse: gibbed bodies can't be hit again.
        WalkBackupIntoPlace(w, ref seq);
        Step(w, ref seq, 1, InputButtons.None, InputButtons.Fire);
        Assert.Equal([BACKUP], w.Deaths.Select(d => d.PeerId));
        Assert.Equal(0, w.Players[VICTIM].Health);
        Assert.True(w.Players[VICTIM].RespawnTicks > 0, "the blast must not restart or cancel the corpse");

        // Delay over: fresh spawn, same skin.
        Step(w, ref seq, SimConfig.RESPAWN_DELAY_TICKS, InputButtons.None);
        Assert.Equal(SimConfig.MAX_HEALTH, w.Players[VICTIM].Health);
        Assert.Equal(0, w.Players[VICTIM].RespawnTicks);
        Assert.Equal(skin, w.Players[VICTIM].Skin);
    }

    [Fact]
    public void Death_LeavesAFrozenCorpseForTheDelay_ThenRespawns()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, new PlayerInput(InputButtons.Fire, AIM_DOWN)); // point blank suicide
        w.Step();

        PlayerState corpse = w.Players[1];
        Assert.Equal(0, corpse.Health);
        Assert.Equal(SimConfig.RESPAWN_DELAY_TICKS, corpse.RespawnTicks);
        Assert.Equal(Vec2.Zero, corpse.Velocity);

        // Held inputs move nothing; the body stays where it died until the end.
        for (int t = 1; t < SimConfig.RESPAWN_DELAY_TICKS; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.Right | InputButtons.Jump));
            w.Step();
        }
        Assert.Equal(corpse.Position, w.Players[1].Position);
        Assert.Equal(1, w.Players[1].RespawnTicks);

        w.EnqueueInput(1, SimConfig.RESPAWN_DELAY_TICKS, new PlayerInput(InputButtons.None));
        w.Step();
        Assert.Equal(0, w.Players[1].RespawnTicks);
        Assert.Equal(SimConfig.MAX_HEALTH, w.Players[1].Health);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, w.Players[1].Ammo);
    }

    [Fact]
    public void DeadState_FreezesMovementAndWeapon()
    {
        PlayerState dead = new PlayerState
        { Position = new Vec2(100, 100), RespawnTicks = 60, Ammo = 3 };

        PlayerState after = PlayerSim.Tick(dead,
            new PlayerInput(InputButtons.Right | InputButtons.Jump), TestWorlds.Flat(), TestWorlds.Stats);
        Assert.Equal(dead, after);

        bool fired = WeaponSim.Tick(ref dead, new PlayerInput(InputButtons.Fire),
            InputButtons.None, TestWorlds.Stats, inputSeq: 0);
        Assert.False(fired);
        Assert.Equal(3, dead.Ammo);
    }

    [Fact]
    public void SnapshotRoundTrips_HealthRespawnAndSpawnImmunity()
    {
        PlayerState[] players =
        [
            new PlayerState
            {
                PeerId = 1,
                Health = 62,
                RespawnTicks = 90,
                SpawnImmunityTicks = 73,
                SpawnImmunityFireThroughSeq = 140,
            },
        ];
        Snapshot restored = Snapshot.Deserialize(new Snapshot(42, players, []).Serialize());
        Assert.Equal(62, restored.Players[0].Health);
        Assert.Equal(90, restored.Players[0].RespawnTicks);
        Assert.Equal(73, restored.Players[0].SpawnImmunityTicks);
        Assert.Equal(140, restored.Players[0].SpawnImmunityFireThroughSeq);
    }

    /// <summary>Left taps + settle put the shooter where the blast reaches the
    /// pinned victim's graze ring; the range check guards the geometry.</summary>
    private static void WalkShooterIntoPlace(SimWorld w, ref int seq)
    {
        Step(w, ref seq, 3, InputButtons.Left);
        Step(w, ref seq, 30, InputButtons.None);
        // Victim's right edge is x=40; the ring is 36..48 px past it.
        Assert.InRange(w.Players[SHOOTER].Position.X, 78, 87);
    }

    /// <summary>Closed-loop walk from x=212 to beside the corpse at the wall;
    /// the range check guards that a blast from here reaches it.</summary>
    private static void WalkBackupIntoPlace(SimWorld w, ref int seq)
    {
        for (int t = 0; t < 120 && w.Players[BACKUP].Position.X > 85; t++)
            Step(w, ref seq, 1, InputButtons.None, InputButtons.Left);
        Step(w, ref seq, 20, InputButtons.None);
        Assert.InRange(w.Players[BACKUP].Position.X, 45, 88);
    }

    private static void Step(SimWorld w, ref int seq, int ticks, InputButtons shooterButtons,
        InputButtons backupButtons = InputButtons.None)
    {
        for (int t = 0; t < ticks; t++)
        {
            w.EnqueueInput(SHOOTER, seq, new PlayerInput(shooterButtons, AIM_DOWN));
            w.EnqueueInput(VICTIM, seq, new PlayerInput(InputButtons.Left));
            if (w.Players.ContainsKey(BACKUP))
                w.EnqueueInput(BACKUP, seq, new PlayerInput(backupButtons, AIM_DOWN));
            seq++;
            w.Step();
        }
    }
}
