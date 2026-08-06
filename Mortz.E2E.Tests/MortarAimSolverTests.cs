using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.E2E.Tests.Harness;
using Xunit;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.E2E.Tests;

/// <summary>Every assertion re-flies the answer through the real MortarSim.
/// Only ever checked against the sim it has to agree with.</summary>
public class MortarAimSolverTests
{
    private const int ARENA_WIDTH = 4000;
    private const int ARENA_HEIGHT = 2000;
    private const float GROUND_Y = 1200;

    private static readonly Combat _combat = new();

    private static PlayerState Player(float x, float y, Vec2 velocity = default) => new()
    {
        PeerId = 1,
        Position = new Vec2(x, y),
        Velocity = velocity,
        Health = (byte)_combat.MaxHealth,
    };

    private static MortarAimQuery Query(PlayerState shooter, PlayerState victim) =>
        new(shooter, victim, _combat, ARENA_WIDTH, ARENA_HEIGHT);

    /// <summary>Fires the solved aim through MortarSim over open ground and
    /// reports the blast damage the victim actually takes.</summary>
    private static int FlyAndMeasure(in MortarAimQuery query, byte aim)
    {
        TerrainMask open = new(ARENA_WIDTH, ARENA_HEIGHT, (_, _) => false, (_, _) => false);
        MortarState shell = WeaponSim.NewShell(
            id: 0, spawnSeq: 0, query.Shooter, new PlayerInput(InputButtons.NONE, aim),
            query.Combat);
        int best = BlastSim.Damage(query.Victim, shell.Position, query.Combat);
        for (int tick = 0; tick < SimConfig.MORTAR_MAX_LIFETIME_TICKS; tick++)
        {
            Vec2 previous = shell.Position;
            MortarOutcome outcome = MortarSim.Tick(ref shell, open, query.Combat, SimConfig.DT);
            Vec2 travel = shell.Position - previous;
            int steps = Math.Max(1, (int)MathF.Ceiling(travel.Length() / 4f));
            for (int step = 1; step <= steps; step++)
            {
                best = Math.Max(best, BlastSim.Damage(
                    query.Victim, previous + travel * (step / (float)steps), query.Combat));
            }
            if (outcome != MortarOutcome.FLYING)
                break;
        }
        return best;
    }

    private static void AssertHits(PlayerState shooter, PlayerState victim)
    {
        MortarAimQuery query = Query(shooter, victim);

        MortarAimSolution solution = MortarAimSolver.Solve(query);

        Assert.True(FlyAndMeasure(query, solution.Aim) >= victim.Health,
            $"aim {solution.Aim} did not reach the victim at {victim.Position}");
    }

    // Level range tops out near v^2/g, about 700 px with the shipped physics.
    [Theory]
    [InlineData(60)]
    [InlineData(150)]
    [InlineData(300)]
    [InlineData(450)]
    [InlineData(620)]
    public void LevelGroundAtAnyRangeIsSolved(float dx)
    {
        AssertHits(Player(500, GROUND_Y), Player(500 + dx, GROUND_Y));
    }

    [Theory]
    [InlineData(-620)]
    [InlineData(-300)]
    [InlineData(-60)]
    public void ShootingLeftIsSolvedToo(float dx)
    {
        AssertHits(Player(2000, GROUND_Y), Player(2000 + dx, GROUND_Y));
    }

    [Theory]
    [InlineData(200, -150)]
    [InlineData(300, -250)]
    [InlineData(-250, -150)]
    public void UphillTargetsAreSolved(float dx, float dy)
    {
        AssertHits(Player(1500, GROUND_Y), Player(1500 + dx, GROUND_Y + dy));
    }

    [Theory]
    [InlineData(300, 250)]
    [InlineData(600, 400)]
    [InlineData(-500, 300)]
    public void DownhillTargetsAreSolved(float dx, float dy)
    {
        AssertHits(Player(1500, GROUND_Y - 600), Player(1500 + dx, GROUND_Y - 600 + dy));
    }

    [Fact]
    public void AVictimStandingOnTheShooterIsSolved()
    {
        AssertHits(Player(900, GROUND_Y), Player(905, GROUND_Y));
    }

    [Theory]
    [InlineData(300, 0)]
    [InlineData(-300, 0)]
    [InlineData(0, -400)]
    public void AMovingShooterIsSolvedThroughInheritedVelocity(float vx, float vy)
    {
        AssertHits(Player(800, GROUND_Y, new Vec2(vx, vy)), Player(1200, GROUND_Y));
    }

    [Fact]
    public void InheritedVelocityActuallyChangesTheAnswer()
    {
        PlayerState victim = Player(1200, GROUND_Y);
        MortarAimQuery still = Query(Player(800, GROUND_Y), victim);
        MortarAimQuery sprinting = Query(Player(800, GROUND_Y, new Vec2(0, -600)), victim);

        // Sanity on the premise: if these matched, the moving-shooter tests
        // would prove nothing about MORTAR_INHERIT.
        Assert.NotEqual(MortarAimSolver.Solve(still).Aim, MortarAimSolver.Solve(sprinting).Aim);
    }

    [Fact]
    public void TheFlattestKillingArcWins()
    {
        // A lob and a flat shot both reach a target at this range; the solver
        // owes the caller the one that spends least time in the air.
        MortarAimQuery query = Query(Player(500, GROUND_Y), Player(1100, GROUND_Y));

        MortarAimSolution solution = MortarAimSolver.Solve(query);

        int slowest = 0;
        for (int aim = 0; aim < 256; aim++)
        {
            if (FlyAndMeasure(query, (byte)aim) >= query.Victim.Health)
                slowest++;
        }
        Assert.True(slowest > 1, "this range should be reachable by more than one aim");
        Assert.True(solution.ImpactTick > 0);
        Assert.Equal(solution.Aim, MortarAimSolver.Solve(query).Aim);
    }

    [Fact]
    public void TheSolutionReportsWhereAndWhenItLands()
    {
        MortarAimQuery query = Query(Player(500, GROUND_Y), Player(1100, GROUND_Y));

        MortarAimSolution solution = MortarAimSolver.Solve(query);

        Assert.True(solution.Damage >= query.Victim.Health);
        Assert.True(solution.ImpactTick > 0);
        Assert.True((solution.Impact - query.Victim.Position).Length()
                    < _combat.MortarCarveRadius * 2);
    }

    [Fact]
    public void AnUnreachableTargetThrowsNamingBothEnds()
    {
        PlayerState shooter = Player(100, GROUND_Y);
        PlayerState victim = Player(3900, GROUND_Y);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => MortarAimSolver.Solve(Query(shooter, victim)));

        Assert.Contains("3800", exception.Message);
        Assert.False(MortarAimSolver.TrySolve(Query(shooter, victim), out _));
    }

    [Fact]
    public void AVictimTougherThanOneShellHasNoSolution()
    {
        // "Lethal" is measured against health, so a victim no single blast can
        // finish has no answer at any range, not a near miss.
        PlayerState shooter = Player(500, GROUND_Y);
        PlayerState armoured = Player(560, GROUND_Y) with
        {
            Health = (byte)(_combat.MortarDamage + 1),
        };

        Assert.False(MortarAimSolver.TrySolve(Query(shooter, armoured), out _));
        Assert.True(MortarAimSolver.TrySolve(
            Query(shooter, Player(560, GROUND_Y) with { Health = (byte)_combat.MortarDamage }),
            out _));
    }

    [Fact]
    public void AWoundedVictimIsKillableWhereAHealthyOneIsNot()
    {
        // The rim only does BlastEdgeDamage, so lowering health widens the set
        // of arcs that count as a kill.
        PlayerState shooter = Player(500, GROUND_Y);
        int reachable = 0;
        int lethalToFullHealth = 0;
        for (int aim = 0; aim < 256; aim++)
        {
            PlayerState wounded = Player(1100, GROUND_Y) with
            {
                Health = (byte)_combat.BlastEdgeDamage,
            };
            if (FlyAndMeasure(Query(shooter, wounded), (byte)aim) >= wounded.Health)
                reachable++;
            PlayerState healthy = Player(1100, GROUND_Y);
            if (FlyAndMeasure(Query(shooter, healthy), (byte)aim) >= healthy.Health)
                lethalToFullHealth++;
        }

        Assert.True(reachable > lethalToFullHealth,
            $"{reachable} arcs kill a wounded victim, {lethalToFullHealth} kill a healthy one");
    }

    [Fact]
    public void ACustomRulesetIsHonouredRatherThanTheConstants()
    {
        // Zero gravity makes straight shots. Answer must be the direct bearing.
        // A solver reading SimConfig instead of Combat would lob.
        Combat floaty = new() { MortarGravity = 0 };
        PlayerState shooter = Player(500, GROUND_Y);
        PlayerState victim = Player(1100, GROUND_Y);
        MortarAimQuery query = new(shooter, victim, floaty, ARENA_WIDTH, ARENA_HEIGHT);

        MortarAimSolution solution = MortarAimSolver.Solve(query);

        Assert.Equal(PlayerInput.AimFromVector(new Vec2(1, 0)), solution.Aim);
    }
}
