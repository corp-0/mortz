using Mortz.Core.Input;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Core.Sim;

public class SimulatedPlayer(PlayerStats stats)
{
    public PlayerState State;
    public InputQueue Inputs { get; } = new();
    public PlayerStats Stats = stats;
    public PlayerStats Effective = stats;
    public List<StatsModifier> Modifiers { get; } = [];
    public Situations Situations;
    public ulong ZoneMask;
    public int? SpawnAssignment;
    public bool HasSpawned;
    public int ModifierRevision;
    public int ModifierEffectiveTick;
}
