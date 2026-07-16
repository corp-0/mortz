using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using CryptoRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Mortz.Server;

/// <summary>Server-side chat and admin auth. Owns its own wire subscriptions,
/// policy, and cleanup, independent of match flow.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerChat : Node
{
    private readonly ChatPolicy _chatPolicy = new();
    private NetworkManager _network = null!;
    private AdminAuthenticator _admin = null!;
    private bool _subscribed;

    [Dependency]
    public ServerHost Host => this.DependOn<ServerHost>();

    [Dependency]
    public IServerSession Session => this.DependOn<IServerSession>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => Start();

    public void OnExitTree() => Stop();

    private void Start()
    {
        if (_subscribed)
            return;
        _network = NetworkManager.Instance;
        _admin = new AdminAuthenticator(Host.AdminPassword);
        _network.PeerJoined += OnPeerJoined;
        _network.PeerLeft += OnPeerLeft;
        ChatSendMsg.Received += OnChatSend;
        AdminAuthRequestMsg.Received += OnAdminAuthRequest;
        AdminProofMsg.Received += OnAdminProof;
        _subscribed = true;
    }

    private void Stop()
    {
        if (!_subscribed)
            return;
        _network.PeerJoined -= OnPeerJoined;
        _network.PeerLeft -= OnPeerLeft;
        ChatSendMsg.Received -= OnChatSend;
        AdminAuthRequestMsg.Received -= OnAdminAuthRequest;
        AdminProofMsg.Received -= OnAdminProof;
        _chatPolicy.Reset();
        _admin.Dispose();
        _subscribed = false;
    }

    private void OnPeerJoined(long peerId, string requestedName)
    {
        if (Session.ContainsPlayer(peerId))
            _admin.Connected(peerId, CryptoRandom.GetBytes(AdminCrypto.SESSION_ID_BYTES));
    }

    private void OnPeerLeft(long peerId)
    {
        _chatPolicy.Remove(peerId);
        _admin.Remove(peerId);
    }

    private void OnChatSend(long sender, ChatSendMsg message)
    {
        if (!Session.ContainsPlayer(sender))
            return;
        if (_chatPolicy.TryAccept(sender, Time.GetTicksMsec(), message.Text,
                out string text, out ChatRejectReason reason))
        {
            new ChatLineMsg(ChatLineKind.PLAYER, sender, Session.PlayerName(sender), text)
                .Broadcast();
            return;
        }

        // No reply to rate-limited senders, otherwise spam amplifies traffic.
        if (reason == ChatRejectReason.RateLimited)
            return;
        string status = reason switch
        {
            ChatRejectReason.Command => "Unknown or invalid command.",
            ChatRejectReason.TooLong =>
                $"Messages are limited to {NetConfig.MAX_CHAT_BYTES} UTF-8 bytes.",
            _ => "Message is empty.",
        };
        SendPrivateSystem(sender, status);
    }

    private void OnAdminAuthRequest(long sender, AdminAuthRequestMsg message)
    {
        if (!Session.IsLobby || !Session.ContainsPlayer(sender))
        {
            new AdminStateMsg(_admin.IsAdmin(sender),
                "Admin authentication is only available in the lobby.")
                .SendTo(sender);
            return;
        }

        byte[] nonce = CryptoRandom.GetBytes(AdminCrypto.NONCE_BYTES);
        AdminChallengeResult result = _admin.Begin(sender, Time.GetTicksMsec(), nonce,
            out byte[] challenge);
        if (result == AdminChallengeResult.STARTED)
        {
            new AdminChallengeMsg(challenge).SendTo(sender);
            return;
        }
        string status = result switch
        {
            AdminChallengeResult.DISABLED => "Admin authentication is disabled on this server.",
            AdminChallengeResult.RATE_LIMITED => "Too many admin attempts. Try again later.",
            _ => "Admin authentication failed.",
        };
        new AdminStateMsg(false, status).SendTo(sender);
    }

    private void OnAdminProof(long sender, AdminProofMsg message)
    {
        if (!Session.IsLobby || !Session.ContainsPlayer(sender))
        {
            new AdminStateMsg(_admin.IsAdmin(sender),
                "Admin authentication is only available in the lobby.")
                .SendTo(sender);
            return;
        }

        AdminProofResult result = _admin.Verify(sender, Time.GetTicksMsec(), message.Proof);
        bool accepted = result == AdminProofResult.ACCEPTED;
        string status = accepted ? "Admin access granted." : result switch
        {
            AdminProofResult.EXPIRED => "Admin challenge expired. Run /admin again.",
            AdminProofResult.NO_CHALLENGE => "No admin challenge is active. Run /admin again.",
            AdminProofResult.DISABLED => "Admin authentication is disabled on this server.",
            _ => "Admin authentication failed.",
        };
        new AdminStateMsg(accepted, status).SendTo(sender);
        if (accepted)
            GD.Print($"[server] player {sender} authenticated as admin");
    }

    private static void SendPrivateSystem(long peerId, string text) =>
        new ChatLineMsg(ChatLineKind.SYSTEM, 0, "Server", text).SendTo(peerId);
}
