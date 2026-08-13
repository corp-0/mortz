using Mortz.Core.Sim;

namespace Mortz.Server.Diagnostics;

/// <summary>Damage waiting for a tick boundary. Error is set when the tick
/// arrives and the target turns out to be gone.</summary>
public readonly record struct DamageRequest(
    int PeerId,
    int Amount,
    Action<DamageOutcome> Done,
    string? Error = null);

public readonly record struct DamageOutcome(
    int AppliedTick,
    int RemainingHealth,
    bool Died,
    string? Error);

/// <summary>A teleport waiting for a tick boundary. Error is set when the tick
/// arrives and the target turns out to be gone or dead.</summary>
public readonly record struct PlacementRequest(
    int PeerId,
    Vec2 Position,
    Action<MutationOutcome> Done,
    string? Error = null);

public readonly record struct MutationOutcome(int AppliedTick, string? Error);

/// <summary>The live match's config, as bytes ready for the wire, plus the
/// arena size.</summary>
public readonly record struct MatchSetupOutcome(
    byte[] Config,
    int TerrainWidth,
    int TerrainHeight);

public readonly record struct WorldStateOutcome(int Tick, E2EWorldPlayer[] Players);

public readonly record struct E2EWorldPlayer(
    int PeerId,
    Vec2 Position,
    Vec2 Velocity,
    int Health,
    int RespawnTicks);
