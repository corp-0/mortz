#if TOOLS
using Mortz.Client.Match;

namespace Mortz.Client.E2E;

/// <summary>What the in-match probe talks to. Provided by ClientMain, so the
/// probe never checks a flag.</summary>
public interface IE2EClientBridge
{
    void AttachMatch(GameView view, LocalPlayerController player);

    void DetachMatch();
}
#endif
