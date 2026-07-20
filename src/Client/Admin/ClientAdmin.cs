using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Admin;

/// <summary>Connected-session admin authority: owns the handshake secrets and
/// signs privileged actions. Human-readable progress goes out as status lines;
/// chat displays them but owns none of this.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientAdmin : Node
{
    private readonly AdminAuthFlow _flow = new();
    private bool _subscribed;

    public bool IsAdmin => _flow.IsAdmin;
    public event Action<bool>? AdminChanged;
    public event Action<string>? StatusLine;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        AdminChallengeMsg.Received += OnChallenge;
        AdminStateMsg.Received += OnState;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        AdminChallengeMsg.Received -= OnChallenge;
        AdminStateMsg.Received -= OnState;
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

    private void OnChallenge(AdminChallengeMsg message)
    {
        if (!_flow.TryAnswerChallenge(Network.LocalPeerId, message))
            StatusLine?.Invoke("Invalid admin challenge.");
    }

    private void OnState(AdminStateMsg message)
    {
        bool wasAdmin = IsAdmin;
        _flow.ApplyState(message);
        StatusLine?.Invoke(message.Status);
        if (wasAdmin != IsAdmin)
            AdminChanged?.Invoke(IsAdmin);
    }
}
