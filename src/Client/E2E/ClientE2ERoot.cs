#if TOOLS
using Godot;
using Mortz.Client.Match;
using Mortz.Shared.E2E;

namespace Mortz.Client.E2E;

/// <summary>
/// Builds the client's E2E nodes in code, and the in-match probe. A .tscn
/// cannot be preprocessed, so a scene that declared these would still
/// reference scripts that #if TOOLS compiles out of an export, and an
/// unresolvable script in the root scene stops the game booting. Do not move
/// these back into ClientMain.tscn or GameView.tscn.
/// </summary>
public static class ClientE2ERoot
{
    public static ClientE2EHandler Attach(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        E2EDriver driver = new() { Name = "E2EDriver" };
        parent.AddChild(driver);
        ClientE2EHandler handler = new() { Name = "E2E" };
        handler.Initialize(driver);
        parent.AddChild(handler);
        return handler;
    }

    /// <summary>The match probe, added once the game view is composed.</summary>
    public static void AttachMatch(GameView view, LocalPlayerController player)
    {
        ArgumentNullException.ThrowIfNull(view);
        E2EMatchProbe probe = new() { Name = "E2EMatchProbe" };
        probe.Initialize(view, player);
        view.AddChild(probe);
    }
}
#endif
