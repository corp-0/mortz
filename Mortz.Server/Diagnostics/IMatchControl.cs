using Mortz.Core.Sim;

namespace Mortz.Server.Diagnostics;

/// <summary>Authoritative mutations applied at a tick boundary, so every
/// outcome the tick produces already accounts for them.</summary>
public interface IMatchControl
{
    /// <summary>Runs queued mutations right before match advancement.</summary>
    void ApplyBefore(SimWorld world);

    /// <summary>Completes pending callbacks with world.Tick as AppliedTick; the
    /// tick's outputs (snapshot, deaths) already include the mutation.</summary>
    void CompleteAfter(SimWorld world);
}
