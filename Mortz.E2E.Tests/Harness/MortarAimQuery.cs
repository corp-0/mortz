using Mortz.Core.Sim;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.E2E.Tests.Harness;

/// <summary>Everything the aim solver needs.</summary>
public readonly record struct MortarAimQuery(
    PlayerState Shooter,
    PlayerState Victim,
    Combat Combat,
    int ArenaWidth,
    int ArenaHeight);
