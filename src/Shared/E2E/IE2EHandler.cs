#if TOOLS
using Mortz.E2E.Protocol;

namespace Mortz.Shared.E2E;

/// <summary>What one process role does with a request. May respond later - a
/// queued mutation completes at a tick boundary, always on the main thread.</summary>
public interface IE2EHandler
{
    E2EProcessRole Role { get; }

    void Handle(E2ERequest request, IE2EResponder responder);
}
#endif
