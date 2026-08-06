#if TOOLS
using Godot;
using Mortz.Server.Diagnostics;
using Mortz.Shared.E2E;

namespace Mortz.Server.E2E;

/// <summary>
/// Builds the server's E2E nodes in code. A .tscn cannot be preprocessed, so a
/// scene that declared these would still reference scripts that #if TOOLS
/// compiles out of an export, and an unresolvable script in the root scene
/// stops the game booting. Do not move these back into ServerMain.tscn.
/// </summary>
public static class ServerE2ERoot
{
    public static ServerE2EHandler Attach(Node parent, E2EMatchControl matchControl)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(matchControl);
        E2EDriver driver = new() { Name = "E2EDriver" };
        parent.AddChild(driver);
        ServerE2EHandler handler = new() { Name = "E2E" };
        handler.Initialize(driver, matchControl);
        parent.AddChild(handler);
        return handler;
    }
}
#endif
