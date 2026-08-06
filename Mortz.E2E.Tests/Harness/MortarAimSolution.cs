using Mortz.Core.Sim;

namespace Mortz.E2E.Tests.Harness;

/// <summary>A fire solution: the aim byte, the tick its shell first reaches the
/// victim, where that happens and the blast damage there.</summary>
public readonly record struct MortarAimSolution(
    byte Aim,
    int ImpactTick,
    Vec2 Impact,
    int Damage);
