using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class MortarTests
{
    private const byte AIM_RIGHT = 0;
    private const byte AIM_DOWN = 64;
    private const byte AIM_UP = 192;
    // Shells are lethal on return: long-running ammo tests fire up-left so the
    // shell dies against the side wall instead of raining back on the shooter.
    private const byte AIM_UP_LEFT = 160;

    private static void StepWith(SimWorld w, ref int seq, InputButtons buttons, byte aim)
    {
        w.EnqueueInput(1, seq++, new PlayerInput(buttons, aim));
        w.Step();
    }

    /// <summary>Flat world where the floor is destructible instead of solid.</summary>
    private static TerrainMask DestructibleFloor() => new(
        TestWorlds.WIDTH, TestWorlds.HEIGHT,
        solid: (x, _) => x < TestWorlds.WALL_LEFT || x >= TestWorlds.WALL_RIGHT,
        destructible: (_, y) => y >= TestWorlds.FLOOR_Y);

    [Fact]
    public void FirePress_SpawnsOneShell_HoldingDoesNotRepeat()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        // Held for 30 ticks: the press edge fires exactly once.
        for (int t = 0; t < 30; t++)
            StepWith(w, ref seq, InputButtons.Fire, AIM_UP);

        Assert.Single(w.Mortars);
        Assert.Equal(1, w.Mortars[0].OwnerId);
        Assert.True(w.Mortars[0].Velocity.Y < 0, "fired up, shell must move up");
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO - 1, w.Players[1].Ammo);
    }

    [Fact]
    public void NoCooldown_BackToBackPressesAllFire()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        for (int shot = 0; shot < 3; shot++)
        {
            StepWith(w, ref seq, InputButtons.Fire, AIM_UP);
            StepWith(w, ref seq, InputButtons.None, AIM_UP);
        }

        Assert.Equal(3, w.Mortars.Count);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO - 3, w.Players[1].Ammo);
    }

    [Fact]
    public void EmptyMagazine_AutoReloadsOneShellPerSecond()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        for (int shot = 0; shot < SimConfig.MORTAR_MAX_AMMO; shot++)
        {
            StepWith(w, ref seq, InputButtons.Fire, AIM_UP_LEFT);
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        }
        Assert.Equal(0, w.Players[1].Ammo);
        Assert.True(w.Players[1].ReloadTicks > 0, "empty magazine must reload by itself");

        // A dry trigger pull neither fires nor scraps the reload.
        int shellsBefore = w.Mortars.Count;
        int reloadBefore = w.Players[1].ReloadTicks;
        StepWith(w, ref seq, InputButtons.Fire, AIM_UP_LEFT);
        Assert.Equal(shellsBefore, w.Mortars.Count);
        Assert.Equal(reloadBefore - 1, w.Players[1].ReloadTicks);

        // Shells bank one per second until the magazine is full.
        for (int t = 0; t < SimConfig.MORTAR_RELOAD_TICKS; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        Assert.Equal(1, w.Players[1].Ammo);

        for (int t = 0; t < 4 * SimConfig.MORTAR_RELOAD_TICKS; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, w.Players[1].Ammo);
        Assert.Equal(0, w.Players[1].ReloadTicks);
    }

    [Fact]
    public void ManualReload_TopsUpOneShellQuickly()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        StepWith(w, ref seq, InputButtons.Fire, AIM_UP); // down one shell
        StepWith(w, ref seq, InputButtons.Reload, AIM_UP);
        Assert.True(w.Players[1].ReloadTicks > 0);

        for (int t = 0; t < SimConfig.MORTAR_RELOAD_TICKS; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_UP);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, w.Players[1].Ammo);
        Assert.Equal(0, w.Players[1].ReloadTicks); // full: reload stopped
    }

    [Fact]
    public void ReloadAtFullMagazine_DoesNothing()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        StepWith(w, ref seq, InputButtons.Reload, AIM_UP);
        Assert.Equal(0, w.Players[1].ReloadTicks);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, w.Players[1].Ammo);
    }

    /// <summary>Shoot 4 of 5, reload for 2 s (banking 2 shells), then fire.
    /// The shot goes out, the shell in progress is lost, the reload stops,
    /// and you're left holding exactly 2 shots.</summary>
    [Fact]
    public void FiringMidReload_KeepsBankedShells_ScrapsTheOneInProgress()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        for (int shot = 0; shot < 4; shot++)
        {
            StepWith(w, ref seq, InputButtons.Fire, AIM_UP_LEFT);
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        }
        Assert.Equal(1, w.Players[1].Ammo);

        StepWith(w, ref seq, InputButtons.Reload, AIM_UP_LEFT);
        for (int t = 0; t < 2 * SimConfig.MORTAR_RELOAD_TICKS; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        Assert.Equal(3, w.Players[1].Ammo); // 1 held + 2 banked

        StepWith(w, ref seq, InputButtons.Fire, AIM_UP_LEFT);
        Assert.Equal(2, w.Players[1].Ammo);
        Assert.Equal(0, w.Players[1].ReloadTicks); // reload died with the shot

        // No resume: nothing arrives on its own.
        for (int t = 0; t < 3 * SimConfig.MORTAR_RELOAD_TICKS; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        Assert.Equal(2, w.Players[1].Ammo);

        // A new reload starts the shell in progress from zero and tops up.
        StepWith(w, ref seq, InputButtons.Reload, AIM_UP_LEFT);
        while (w.Players[1].ReloadTicks > 0)
            StepWith(w, ref seq, InputButtons.None, AIM_UP_LEFT);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, w.Players[1].Ammo);
    }

    [Fact]
    public void ShellFallsInAnArc()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        StepWith(w, ref seq, InputButtons.Fire, AIM_RIGHT);
        MortarState launched = w.Mortars[0];

        for (int t = 0; t < 3; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_RIGHT);

        MortarState later = w.Mortars[0];
        Assert.True(later.Position.X > launched.Position.X, "keeps flying right");
        Assert.True(later.Velocity.Y > launched.Velocity.Y, "gravity bends the arc down");
    }

    [Fact]
    public void ImpactOnDestructible_CarvesAndReportsExplosion()
    {
        SimWorld w = new SimWorld(DestructibleFloor(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;
        Vec2 feet = w.Players[1].Position;

        // Point blank into the floor.
        StepWith(w, ref seq, InputButtons.Fire, AIM_DOWN);

        Assert.Empty(w.Mortars);
        (int x, int y, int radius, int owner, int spawnSeq) = Assert.Single(w.Explosions);
        Assert.Equal(SimConfig.MORTAR_CARVE_RADIUS, radius);
        Assert.Equal(1, owner);
        Assert.Equal(0, spawnSeq); // fired by the very first input
        Assert.True(Math.Abs(x - feet.X) < 10 && y > feet.Y - 10, $"impact near the feet, got ({x},{y})");
        Assert.Equal(TerrainMaterial.Empty, w.Terrain.Get(x, y));

        // Events last one Step only.
        StepWith(w, ref seq, InputButtons.None, AIM_DOWN);
        Assert.Empty(w.Explosions);
    }

    [Fact]
    public void ImpactOnSolid_ExplodesWithoutCarving()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        StepWith(w, ref seq, InputButtons.Fire, AIM_DOWN);
        for (int t = 0; t < 5 && w.Mortars.Count > 0; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_DOWN);

        Assert.Empty(w.Mortars);
        (int x, int y, _, _, _) = Assert.Single(w.Explosions);
        Assert.Equal(TerrainMaterial.Solid, w.Terrain.Get(x, y));
    }

    [Fact]
    public void ShellLeavingTheMapBottom_ExplodesAtBoundary()
    {
        // No terrain at all: everything fired downward just falls out.
        TerrainMask world = new TerrainMask(400, 300, (_, _) => false, (_, _) => false);
        SimWorld w = new SimWorld(world, TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;

        StepWith(w, ref seq, InputButtons.Fire, AIM_DOWN);
        Assert.Single(w.Mortars);

        int explosions = 0;
        for (int t = 0; t < SimConfig.TICK_RATE && w.Mortars.Count > 0; t++)
        {
            StepWith(w, ref seq, InputButtons.None, AIM_DOWN);
            explosions += w.Explosions.Count;
        }
        Assert.Empty(w.Mortars);
        Assert.Equal(1, explosions);
        Assert.True(w.Explosions[0].Y >= world.Height);
    }

    [Fact]
    public void ShellReachingMaxLifetime_DetonatesAtItsCurrentPosition()
    {
        MatchConfig config = new() { MortarSpeed = 100, MortarGravity = 0 };
        config.Clamp();
        SimWorld w = new SimWorld(TestWorlds.Flat(), config);
        w.AddPlayer(1);
        int seq = 0;

        StepWith(w, ref seq, InputButtons.Fire, AIM_UP);
        List<(int X, int Y, int Radius, int OwnerId, int SpawnSeq)> explosions = new();
        for (int t = 1; t <= SimConfig.MORTAR_MAX_LIFETIME_TICKS && w.Mortars.Count > 0; t++)
        {
            StepWith(w, ref seq, InputButtons.None, AIM_UP);
            explosions.AddRange(w.Explosions);
        }

        Assert.Empty(w.Mortars);
        (int X, int Y, int Radius, int OwnerId, int SpawnSeq) explosion = Assert.Single(explosions);
        Assert.Equal(1, explosion.OwnerId);
        Assert.Equal(0, explosion.SpawnSeq);
        Assert.True(explosion.Y < 0, "the long-lived upward shell should detonate above the map");
    }

    [Fact]
    public void SelfBlast_KillsAndRespawnsTheShooter()
    {
        SimWorld w = new SimWorld(DestructibleFloor(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;
        StepWith(w, ref seq, InputButtons.Fire, AIM_UP); // spend one shell first
        StepWith(w, ref seq, InputButtons.None, AIM_UP);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO - 1, w.Players[1].Ammo);

        // Point blank into the floor: the blast covers the shooter.
        StepWith(w, ref seq, InputButtons.Fire, AIM_DOWN);

        (int peerId, Vec2 pos, int killerId, bool owned) = Assert.Single(w.Deaths);
        Assert.Equal(1, peerId);
        Assert.Equal(1, killerId); // own shell: killer is the victim
        Assert.False(owned); // suicide, not a parry
        Assert.True(pos.Y > 200, "died near the floor");
        Assert.True(w.Players[1].RespawnTicks > 0, "gibbed bodies lie dead for a while");

        for (int t = 0; t < SimConfig.RESPAWN_DELAY_TICKS; t++)
            StepWith(w, ref seq, InputButtons.None, AIM_DOWN);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, w.Players[1].Ammo); // fresh spawn, full mag
        // The crater ate the spawn column's floor, so FindSpawn falls back to
        // dropping the fresh body mid-air, well above the death spot.
        Assert.True(w.Players[1].Position.Y < 200, $"respawned high up, got Y={w.Players[1].Position.Y}");
    }

    [Fact]
    public void DirectHit_ExplodesOnContact_KillsTheTarget_NotTheShooter()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1); // spawns at x=241
        w.AddPlayer(2); // spawns at x=130
        int seq = 0;

        // Walk player 2 to the right, well clear of both spawns, then stop.
        for (int t = 0; t < 90; t++)
        {
            w.EnqueueInput(1, seq, new PlayerInput(InputButtons.None));
            w.EnqueueInput(2, seq, new PlayerInput(InputButtons.Right));
            seq++;
            w.Step();
        }
        Vec2 target = w.Players[2].Position;
        Assert.True(target.X > 320, $"target should be far right, got {target.X}");

        // Player 1 fires straight right; collect deaths while the shell flies.
        List<int> killed = new List<int>();
        for (int t = 0; t < 30 && killed.Count == 0; t++)
        {
            InputButtons fire = t == 0 ? InputButtons.Fire : InputButtons.None;
            w.EnqueueInput(1, seq, new PlayerInput(fire, AIM_RIGHT));
            w.EnqueueInput(2, seq, new PlayerInput(InputButtons.None));
            seq++;
            w.Step();
            foreach ((int id, Vec2 _, int killer, bool _) in w.Deaths)
            {
                killed.Add(id);
                Assert.Equal(1, killer); // the shooter gets the credit
            }
        }

        Assert.Equal([2], killed); // the shooter outlived their own shot

        for (int t = 0; t < SimConfig.RESPAWN_DELAY_TICKS; t++)
        {
            w.EnqueueInput(1, seq, new PlayerInput(InputButtons.None));
            w.EnqueueInput(2, seq, new PlayerInput(InputButtons.None));
            seq++;
            w.Step();
        }
        Assert.True(w.Players[2].Position.X < 200, "victim respawned back at their spawn column");
    }

    [Fact]
    public void SnapshotRoundTrips_Mortars()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        w.AddPlayer(1);
        int seq = 0;
        StepWith(w, ref seq, InputButtons.Fire, AIM_UP);
        StepWith(w, ref seq, InputButtons.None, AIM_UP);

        Snapshot original = w.TakeSnapshot();
        Snapshot restored = Snapshot.Deserialize(original.Serialize());

        Assert.Equal(original.Players[0].Ammo, restored.Players[0].Ammo);
        Assert.Equal(original.Players[0].ReloadTicks, restored.Players[0].ReloadTicks);

        MortarState sent = Assert.Single(original.Mortars);
        MortarState got = Assert.Single(restored.Mortars);
        Assert.Equal(sent.Id, got.Id);
        Assert.Equal(sent.OwnerId, got.OwnerId);
        Assert.Equal(sent.Position.X, got.Position.X, 0.126f);
        Assert.Equal(sent.Position.Y, got.Position.Y, 0.126f);
        Assert.Equal(sent.Velocity.X, got.Velocity.X, 0.126f);
        Assert.Equal(sent.Velocity.Y, got.Velocity.Y, 0.126f);
    }
}
