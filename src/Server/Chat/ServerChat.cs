using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Admin;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Text;
using Mortz.Net;
using Mortz.Server.Hosting;
using Mortz.Server.Lobby;
using Mortz.Server.Session;
using CryptoRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Mortz.Server.Chat;

/// <summary>Server-side chat and admin auth. Owns its own wire subscriptions,
/// policy, and cleanup, independent of match flow.</summary>
[Meta(typeof(IAutoNode))]
public partial class ServerChat : Node, IServerAdminAuthorizer
{
    private readonly ChatPolicy _chatPolicy = new();
    private readonly HashSet<long> _typing = new();
    private AdminAuthenticator _admin = null!;
    private bool _subscribed;

    [Dependency]
    private ServerBootConfig Config => this.DependOn<ServerBootConfig>();

    [Dependency]
    private IServerSession Session => this.DependOn<IServerSession>();

    [Dependency]
    private IServerLobbySettings LobbySettings => this.DependOn<IServerLobbySettings>();

    [Dependency]
    private NetworkManager Network => this.DependOn<NetworkManager>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => Start();

    public void OnExitTree() => Stop();

    public bool TryAuthorize(long peerId, ulong sequence, byte action,
        ReadOnlySpan<byte> payload, ReadOnlySpan<byte> tag) =>
        _subscribed && _admin.VerifyCommand(peerId, sequence, action, payload, tag);

    private void Start()
    {
        if (_subscribed)
            return;
        _admin = new AdminAuthenticator(Config.AdminPassword);
        Network.PeerJoined += OnPeerJoined;
        Network.PeerLeft += OnPeerLeft;
        ChatSendMsg.Received += OnChatSend;
        RollRequestMsg.Received += OnRollRequest;
        TypingMsg.Received += OnTyping;
        AdminAuthRequestMsg.Received += OnAdminAuthRequest;
        AdminProofMsg.Received += OnAdminProof;
        LobbySettings.Changed += OnLobbySettingsChanged;
        _subscribed = true;
    }

    private void Stop()
    {
        if (!_subscribed)
            return;
        Network.PeerJoined -= OnPeerJoined;
        Network.PeerLeft -= OnPeerLeft;
        ChatSendMsg.Received -= OnChatSend;
        RollRequestMsg.Received -= OnRollRequest;
        TypingMsg.Received -= OnTyping;
        AdminAuthRequestMsg.Received -= OnAdminAuthRequest;
        AdminProofMsg.Received -= OnAdminProof;
        LobbySettings.Changed -= OnLobbySettingsChanged;
        _typing.Clear();
        _chatPolicy.Reset();
        _admin.Dispose();
        _subscribed = false;
    }

    private void OnPeerJoined(long peerId, string requestedName)
    {
        if (!Session.ContainsPlayer(peerId)) return;

        _admin.Connected(peerId, CryptoRandom.GetBytes(AdminCrypto.SESSION_ID_BYTES));
        RichText joinedText = new RichText()
            .Add(requestedName, new Style().Bold())
            .Add(" joined the game.");
        ChatProtocol.Broadcast(new ChatLine.System(joinedText));
    }

    private void OnPeerLeft(long peerId)
    {
        _chatPolicy.Remove(peerId);
        _admin.Remove(peerId);
        if (_typing.Remove(peerId))
            new TypingStateMsg(peerId, false).Broadcast();
        RichText leftText = new RichText()
            .Add(Session.PlayerName(peerId), new Style().Bold())
            .Add(" left the game.");
        ChatProtocol.Broadcast(new ChatLine.System(leftText));
    }

    // Dropping no-op repeats is the flood guard: one broadcast per state flip.
    private void OnTyping(long sender, TypingMsg message)
    {
        if (!Session.ContainsPlayer(sender))
            return;
        bool changed = message.IsTyping ? _typing.Add(sender) : _typing.Remove(sender);
        if (changed)
            new TypingStateMsg(sender, message.IsTyping).Broadcast();
    }

    private void OnChatSend(long sender, ChatSendMsg message)
    {
        if (!Session.ContainsPlayer(sender))
            return;
        if (_chatPolicy.TryAccept(sender, Time.GetTicksMsec(), message.Text,
                out string text, out ChatRejectReason reason))
        {
            ChatProtocol.Broadcast(
                new ChatLine.Player(sender, Session.PlayerName(sender), text));
            return;
        }

        // No reply to rate-limited senders, otherwise spam amplifies traffic.
        if (reason == ChatRejectReason.RATE_LIMITED)
            return;
        string status = reason switch
        {
            ChatRejectReason.COMMAND => "Unknown or invalid command.",
            ChatRejectReason.TOO_LONG =>
                $"Messages are limited to {NetConfig.MAX_CHAT_BYTES} UTF-8 bytes.",
            _ => "Message is empty.",
        };
        SendPrivateSystem(sender, status);
    }

    // Silent when rate limited, same as chat: replying would amplify spam.
    private void OnRollRequest(long sender, RollRequestMsg message)
    {
        if (!Session.ContainsPlayer(sender) ||
            !_chatPolicy.TryAcceptRoll(sender, Time.GetTicksMsec()))
            return;
        int value = Random.Shared.Next(DiceRoll.MIN, DiceRoll.MAX + 1);
        ChatProtocol.Broadcast(
            new ChatLine.Roll(sender, Session.PlayerName(sender), value));
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
        string status = accepted
            ? "Admin access granted."
            : result switch
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

    private void OnLobbySettingsChanged(LobbySettingsChange change)
    {
        string adminName = Session.PlayerName(change.AdminId);

        foreach (LobbySettingDelta delta in change.Deltas)
        {
            RichText text = new RichText()
                .Add(adminName, new Style().Color(RichTextColor.ACID))
                .Add(" changed ")
                .Add(delta.Name, new Style().Bold())
                .Add($" {delta.Before}")
                .Add(" > ")
                .Add(delta.After);

            ChatProtocol.Broadcast(new ChatLine.System(text));
        }
    }

    private static void SendPrivateSystem(long peerId, string text) =>
        ChatProtocol.SendTo(peerId, new ChatLine.System(text));
}
