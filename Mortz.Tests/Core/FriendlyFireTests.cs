using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

/// <summary>
/// Friendly fire through the full sim, on the HealthTests geometry: the victim
/// pinned at the left wall, the shooter suicide-firing at their own feet from
/// the victim's graze ring.
/// </summary>
public class FriendlyFireTests
{
    private const byte AIM_DOWN = 64;
    private const int SHOOTER = 27; // x=91; 3 left taps drift to ~83
    private const int VICTIM = 30;  // x=62; walks left, pins at x=24

    [Fact]
    public void FriendlyFireOff_SparesTheTeammate_NeverTheShooter()
    {
        SimWorld w = World(friendlyFire: false, shooterTeam: 1, victimTeam: 1);
        int seq = 0;
        FireTheGrazeShot(w, ref seq);

        Assert.Equal([SHOOTER], w.Deaths.Select(d => d.PeerId)); // suicide still lands
        Assert.Equal(SimConfig.MAX_HEALTH, w.Players[VICTIM].Health);
    }

    [Fact]
    public void FriendlyFireOff_StillHurtsEnemies()
    {
        SimWorld w = World(friendlyFire: false, shooterTeam: 1, victimTeam: 2);
        int seq = 0;
        FireTheGrazeShot(w, ref seq);

        Assert.True(w.Players[VICTIM].Health < SimConfig.MAX_HEALTH, "an enemy graze must hurt");
    }

    [Fact]
    public void TeamId_RidesTheSnapshot_AndSurvivesRespawn()
    {
        SimWorld w = World(friendlyFire: true, shooterTeam: 1, victimTeam: 2);
        int seq = 0;

        Snapshot restored = Snapshot.Deserialize(w.TakeSnapshot().Serialize());
        Assert.Equal(1, restored.Players.Single(p => p.PeerId == SHOOTER).TeamId);
        Assert.Equal(2, restored.Players.Single(p => p.PeerId == VICTIM).TeamId);

        FireTheGrazeShot(w, ref seq); // shooter suicides
        Step(w, ref seq, SimConfig.RESPAWN_DELAY_TICKS + 1, InputButtons.None);
        Assert.Equal(0, w.Players[SHOOTER].RespawnTicks);
        Assert.Equal(1, w.Players[SHOOTER].TeamId);
    }

    private static SimWorld World(bool friendlyFire, byte shooterTeam, byte victimTeam)
    {
        MatchConfig cfg = new() { Teams = true, FriendlyFire = friendlyFire };
        SimWorld w = new SimWorld(TestWorlds.Flat(), cfg);
        w.AddPlayer(SHOOTER, shooterTeam);
        w.AddPlayer(VICTIM, victimTeam);
        return w;
    }

    /// <summary>Pin the victim, walk the shooter into the graze ring, fire at
    /// the floor. The range guard asserts the blast reaches the victim, so a
    /// spared teammate proves friendly fire and not distance.</summary>
    private static void FireTheGrazeShot(SimWorld w, ref int seq)
    {
        Step(w, ref seq, 60, InputButtons.None);
        Assert.InRange(w.Players[VICTIM].Position.X, 23, 26);
        Step(w, ref seq, 3, InputButtons.Left);
        Step(w, ref seq, 30, InputButtons.None);
        Assert.InRange(w.Players[SHOOTER].Position.X, 78, 87);

        PlayerState victimBefore = w.Players[VICTIM];
        Step(w, ref seq, 1, InputButtons.Fire);
        (int ex, int ey, _, _, _) = Assert.Single(w.Explosions);
        Assert.True(BlastSim.Damage(victimBefore, new Vec2(ex, ey), w.Config) > 0,
            "setup guard: the blast must reach the victim");
    }

    private static void Step(SimWorld w, ref int seq, int ticks, InputButtons shooterButtons)
    {
        for (int t = 0; t < ticks; t++)
        {
            w.EnqueueInput(SHOOTER, seq, new PlayerInput(shooterButtons, AIM_DOWN));
            w.EnqueueInput(VICTIM, seq, new PlayerInput(InputButtons.Left));
            seq++;
            w.Step();
        }
    }
}
