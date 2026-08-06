#if TOOLS
using Mortz.E2E.Protocol;

namespace Mortz.Shared.E2E;

/// <summary>Structured stdout, seen from a handler. Main thread only.</summary>
public interface IE2EResponder
{
    void Respond(E2EResponse response);

    void Emit(E2EEvent evt);
}
#endif
