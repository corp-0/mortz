using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Net;
using Mortz.Core.Net.Admin;
using Mortz.Net;

namespace Mortz.Client.Admin;

/// <summary>Connected-session admin authority: owns the handshake secrets and
/// signs privileged actions. Human-readable progress goes out as status lines;
/// chat displays them but owns none of this.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientAdmin : Node,
    IHandle<AdminChallengeMsg>,
    IHandle<AdminStateMsg>
{
    private AdminAuthFlow _flow = null!;
    private bool _subscribed;

    public bool IsAdmin => _flow is { IsAdmin: true };
    public event Action<bool>? AdminChanged;
    public event Action<string>? StatusLine;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private IClientSender Sender => this.DependOn<IClientSender>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _flow = new AdminAuthFlow(Sender);
        Router.Add(this);
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Router.Remove(this);
        _subscribed = false;
    }

    public void BeginAuthentication(string password)
    {
        if (Network.LocalPeerId == 0)
        {
            StatusLine?.Invoke("Connect to a server before authenticating.");
            return;
        }
        bool wasAdmin = IsAdmin;
        _flow.Begin(password);
        if (wasAdmin)
            AdminChanged?.Invoke(false);
        StatusLine?.Invoke("Requesting admin challenge...");
    }

    public bool TrySignAdminAction(byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag) =>
        _flow.TrySign(Network.LocalPeerId, action, payload, out sequence, out tag);

    public void Handle(in AdminChallengeMsg message)
    {
        if (!_flow.TryAnswerChallenge(Network.LocalPeerId, message))
            StatusLine?.Invoke("Invalid admin challenge.");
    }

    public void Handle(in AdminStateMsg message)
    {
        bool wasAdmin = IsAdmin;
        _flow.ApplyState(message);
        StatusLine?.Invoke(message.Status);
        if (wasAdmin != IsAdmin)
            AdminChanged?.Invoke(IsAdmin);
    }
}
