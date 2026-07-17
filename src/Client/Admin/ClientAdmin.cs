using Godot;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Admin;

/// <summary>Connected-session admin authority: owns the handshake secrets and
/// signs privileged actions. Human-readable progress goes out as status lines;
/// chat displays them but owns none of this.</summary>
public partial class ClientAdmin : Node
{
    private readonly AdminAuthFlow _flow = new();
    private long _localPeerId;

    public bool IsAdmin => _flow.IsAdmin;
    public event Action<bool>? AdminChanged;
    public event Action<string>? StatusLine;

    public override void _Ready()
    {
        AdminChallengeMsg.Received += OnChallenge;
        AdminStateMsg.Received += OnState;
        NetworkManager network = NetworkManager.Instance;
        network.Connected += OnConnected;
        network.ConnectionFailed += OnSessionEnded;
        network.Disconnected += OnSessionEnded;
        network.TransportReset += OnSessionEnded;
    }

    public override void _ExitTree()
    {
        AdminChallengeMsg.Received -= OnChallenge;
        AdminStateMsg.Received -= OnState;
        NetworkManager network = NetworkManager.Instance;
        network.Connected -= OnConnected;
        network.ConnectionFailed -= OnSessionEnded;
        network.Disconnected -= OnSessionEnded;
        network.TransportReset -= OnSessionEnded;
        OnSessionEnded();
    }

    public void BeginAuthentication(string password)
    {
        if (_localPeerId == 0)
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
        _flow.TrySign(_localPeerId, action, payload, out sequence, out tag);

    internal void SetLocalPeerIdForTest(long peerId) => _localPeerId = peerId;

    private void OnConnected()
    {
        _flow.Reset();
        _localPeerId = Multiplayer.GetUniqueId();
    }

    private void OnSessionEnded()
    {
        bool changed = IsAdmin;
        _flow.Reset();
        _localPeerId = 0;
        if (changed)
            AdminChanged?.Invoke(false);
    }

    private void OnChallenge(AdminChallengeMsg message)
    {
        if (!_flow.TryAnswerChallenge(_localPeerId, message))
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
