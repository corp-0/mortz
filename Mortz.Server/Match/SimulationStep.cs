using Mortz.Core.Sim;

namespace Mortz.Server.Match;

/// <summary>Advances the authoritative simulation and publishes its tick outputs.</summary>
public class SimulationStep : IMatchStep
{
    public void Advance(MatchTick tick)
    {
        SimWorld world = tick.Match.World;
        world.Step();
        tick.SetSimulationOutputs(
            world.MortarEvents, world.Explosions, world.ShellRetirements, world.Deaths);
    }
}
