using Mortz.Core.Sim;
using Mortz.Core.Terrain;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Which aim byte hits. There are only 256 aims, so this flies every one of
/// them through the real <see cref="MortarSim"/> and keeps the best rather
/// than solving a parabola: the muzzle offset makes the launch point depend on
/// the answer, fall speed is clamped, the shooter's velocity is inherited, and
/// the angle has to land on one of 256 directions. Closed-form math would be
/// wrong on all four counts, and would drift the day the physics changes.
///
/// Terrain is not consulted: the solution assumes a clear arc, which is why
/// ties go to the flattest trajectory that still kills.
/// </summary>
public static class MortarAimSolver
{
    private const int AIM_COUNT = 256;

    /// <summary>Matches MortarSim's own substep, so a fast shell cannot skip
    /// past the victim between two sampled points.</summary>
    private const float SAMPLE_STEP = 4f;

    public static MortarAimSolution Solve(in MortarAimQuery query)
    {
        if (TrySolve(query, out MortarAimSolution solution))
            return solution;

        Vec2 from = BodyCenter(query.Shooter);
        Vec2 to = BodyCenter(query.Victim);
        throw new ArgumentException(
            $"No aim reaches the victim: shooter body centre {from}, victim body centre {to}, " +
            $"{(to - from).Length():0.#} px apart in a {query.ArenaWidth}x{query.ArenaHeight} arena. " +
            "The solver ignores terrain, so this is out of range, not blocked.",
            nameof(query));
    }

    /// <summary>False when no aim brings a shell close enough to kill.</summary>
    public static bool TrySolve(in MortarAimQuery query, out MortarAimSolution solution)
    {
        TerrainMask open = new(
            Math.Max(1, query.ArenaWidth),
            Math.Max(1, query.ArenaHeight),
            (_, _) => false,
            (_, _) => false);

        solution = default;
        bool found = false;
        for (int aim = 0; aim < AIM_COUNT; aim++)
        {
            if (!TryFly(query, open, (byte)aim, out MortarAimSolution candidate))
                continue;
            // The flattest arc that still kills: least time in the air, least
            // that the ignored terrain can get in the way of.
            if (found && candidate.ImpactTick >= solution.ImpactTick)
                continue;
            solution = candidate;
            found = true;
        }
        return found;
    }

    private static bool TryFly(
        in MortarAimQuery query, TerrainMask open, byte aim, out MortarAimSolution solution)
    {
        solution = default;
        MortarState shell = WeaponSim.NewShell(
            id: 0, spawnSeq: 0, query.Shooter, new PlayerInput(InputButtons.NONE, aim),
            query.Combat);
        // The muzzle can already be inside the victim.
        if (Lethal(query, shell.Position, out int muzzleDamage))
        {
            solution = new MortarAimSolution(aim, 0, shell.Position, muzzleDamage);
            return true;
        }

        for (int tick = 1; tick <= SimConfig.MORTAR_MAX_LIFETIME_TICKS; tick++)
        {
            Vec2 previous = shell.Position;
            MortarOutcome outcome = MortarSim.Tick(ref shell, open, query.Combat, SimConfig.DT);
            if (Crosses(query, previous, shell.Position, out Vec2 at, out int damage))
            {
                solution = new MortarAimSolution(aim, tick, at, damage);
                return true;
            }
            if (outcome != MortarOutcome.FLYING)
                return false;
        }
        return false;
    }

    /// <summary>Walks the tick's travel in small steps, so the answer does not
    /// depend on how far a shell happens to move per tick.</summary>
    private static bool Crosses(
        in MortarAimQuery query, Vec2 from, Vec2 to, out Vec2 at, out int damage)
    {
        Vec2 travel = to - from;
        float distance = travel.Length();
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / SAMPLE_STEP));
        for (int step = 1; step <= steps; step++)
        {
            Vec2 sample = from + travel * (step / (float)steps);
            if (!Lethal(query, sample, out damage))
                continue;
            at = sample;
            return true;
        }
        at = default;
        damage = 0;
        return false;
    }

    /// <summary>Lethal, not merely damaging: the caller wants the victim dead,
    /// and the real blast falloff is the only honest judge of that.</summary>
    private static bool Lethal(in MortarAimQuery query, Vec2 center, out int damage)
    {
        damage = BlastSim.Damage(query.Victim, center, query.Combat);
        return damage >= query.Victim.Health;
    }

    private static Vec2 BodyCenter(in PlayerState player) =>
        player.Position with { Y = player.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };
}
