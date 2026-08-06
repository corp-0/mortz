using Mortz.Core.Sim;

namespace Mortz.Server.Diagnostics;

/// <summary>A teleport waiting for a tick boundary. Error is set when the tick
/// arrives and the target turns out to be gone or dead.</summary>
public readonly record struct PlacementRequest(
    int PeerId,
    Vec2 Position,
    Action<MutationOutcome> Done,
    string? Error = null);
