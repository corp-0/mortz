#if TOOLS
using Chickensoft.AutoInject;
using Mortz.Client.E2E;

namespace Mortz.Client;

public partial class ClientMain : IProvide<IE2EClientBridge>
{
    private IE2EClientBridge _bridge = new NullE2EClientBridge();

    IE2EClientBridge IProvide<IE2EClientBridge>.Value() => _bridge;

    partial void OnToolsReady()
    {
        if (Shared.E2E.E2ELaunch.Enabled)
            _bridge = ClientE2ERoot.Attach(this);
    }
}
#endif
