#if TOOLS
using Mortz.Client.Match;

namespace Mortz.Client.E2E;

public sealed class NullE2EClientBridge : IE2EClientBridge
{
    public void AttachMatch(GameView view, LocalPlayerController player) { }

    public void DetachMatch() { }
}
#endif
