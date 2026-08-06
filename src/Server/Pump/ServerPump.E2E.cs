#if TOOLS
using Mortz.Server.Diagnostics;
using Mortz.Server.E2E;
using Mortz.Shared.E2E;

namespace Mortz.Server.Pump;

public partial class ServerPump
{
    internal ServerE2EHandler? E2E { get; private set; }

    partial void AttachE2E(ref IMatchObserver observer, ref IMatchControl control)
    {
        if (!E2ELaunch.Enabled)
            return;

        E2EMatchControl matchControl = new();
        E2E = ServerE2ERoot.Attach(this, matchControl);
        observer = E2E;
        control = matchControl;
    }
}
#endif
