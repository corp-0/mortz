using Mortz.Core.Sim;

namespace Mortz.Server.Diagnostics;

public sealed class NullMatchControl : IMatchControl
{
    public void ApplyBefore(SimWorld world) { }

    public void CompleteAfter(SimWorld world) { }
}
