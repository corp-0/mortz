using Mortz.Core.Match;
using Mortz.Core.Sim;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.E2E.Tests.Harness;

/// <summary>Everything the aim solver needs. Arena bounds stand in for terrain:
/// the solver never consults it, so the bounds only decide when a shell has
/// left play.</summary>
public readonly record struct MortarAimQuery(
    PlayerState Shooter,
    PlayerState Victim,
    Combat Combat,
    int ArenaWidth,
    int ArenaHeight);
