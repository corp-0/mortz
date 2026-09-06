using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

public record Snapshot(int Tick, PlayerState[] Players, MortarState[] Mortars);
