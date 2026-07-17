using System.Security.Cryptography;
using Mortz.Core.Admin;
using Mortz.Core.Chat;
using Mortz.Core.Chat.Commands;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>Connected-session chat and command state. Outlives any view, so
/// rearranging the UI keeps history, command state, and admin authority.</summary>
public sealed class ClientChatSession : IDisposable
{
    private readonly ChatCommandRegistry<ClientChatSession> _commands = new();
    private byte[]? _pendingPasswordKey;
    private byte[]? _pendingAdminKey;
    private byte[]? _adminKey;
    private long _localPeerId;
    private ulong _nextAdminSequence = 1;
    private bool _subscribed;

    public ClientChatSession() => _commands.RegisterAssemblyCommands();

    public ChatState State { get; } = new();
    public bool IsAdmin => _adminKey != null;
    public IEnumerable<ChatCommandMetadata> CommandCatalog => _commands.Commands;
    public event Action<bool>? AdminChanged;

    public void Subscribe()
    {
        if (_subscribed)
            return;
        ChatLineMsg.Received += OnChatLine;
        AdminChallengeMsg.Received += OnAdminChallenge;
        AdminStateMsg.Received += OnAdminState;
        _subscribed = true;
    }

    public void Begin()
    {
        ClearSecrets();
        _localPeerId = 0;
        State.Clear();
    }

    public void SetLocalPeerId(long peerId) => _localPeerId = peerId;

    public bool Submit(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        if (input.TrimStart().StartsWith('/'))
        {
            if (!_commands.TryParse(input.TrimStart(),
                    out ChatCommand<ClientChatSession>? command, out string parseError))
            {
                State.AddSystem(parseError, isPrivate: true);
                return false;
            }
            command!.Execute(this);
            return true;
        }

        if (!ChatTextSanitizer.TrySanitize(input, out string text, out ChatRejectReason reason))
        {
            string error = reason == ChatRejectReason.TOO_LONG
                ? $"Messages are limited to {NetConfig.MAX_CHAT_BYTES} UTF-8 bytes."
                : "Message is empty.";
            State.AddSystem(error, isPrivate: true);
            return false;
        }
        new ChatSendMsg(text).SendToServer();
        return true;
    }

    /// <summary>Creates the proof for a future privileged mutation. The caller
    /// serializes this sequence, action, payload, and tag into its action message.</summary>
    public bool TrySignAdminAction(byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag)
    {
        sequence = 0;
        tag = [];
        if (_adminKey == null || _localPeerId == 0)
            return false;
        sequence = _nextAdminSequence++;
        tag = AdminCrypto.ComputeCommandTag(_adminKey, _localPeerId, sequence, action, payload);
        return true;
    }

    public void End()
    {
        bool changed = IsAdmin;
        ClearSecrets();
        _localPeerId = 0;
        State.Clear();
        if (changed)
            AdminChanged?.Invoke(false);
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            ChatLineMsg.Received -= OnChatLine;
            AdminChallengeMsg.Received -= OnAdminChallenge;
            AdminStateMsg.Received -= OnAdminState;
            _subscribed = false;
        }
        End();
    }

    internal void ShowCommandHelp()
    {
        foreach (ChatCommandMetadata metadata in CommandCatalog)
        {
            RichText line = new RichText()
                .Bold().ApplyTo(metadata.Usage)
                .Add(" - ").Add(metadata.Description);
            State.AddSystem(line, isPrivate: true);
        }
    }

    internal void BeginAdminAuthentication(string password)
    {
        if (_localPeerId == 0)
        {
            State.AddSystem("Connect to a server before authenticating.", isPrivate: true);
            return;
        }
        bool wasAdmin = IsAdmin;
        ClearAdminKey();
        if (wasAdmin)
            AdminChanged?.Invoke(false);
        ClearPendingSecrets();
        _pendingPasswordKey = AdminCrypto.DerivePasswordKey(password);
        new AdminAuthRequestMsg().SendToServer();
        State.AddSystem("Requesting admin challenge...", isPrivate: true);
    }

    private void OnChatLine(ChatLineMsg message)
    {
        if (!Enum.IsDefined(message.Kind) || string.IsNullOrWhiteSpace(message.Text))
            return;
        if (message.Kind == ChatLineKind.PLAYER)
            State.Add(new ChatEntry(ChatEntryKind.PLAYER, message.SenderId,
                message.SenderName, message.Text, ChatTextFormat.MARKDOWN));
        else
            State.AddSystem(message.Text);
    }

    private void OnAdminChallenge(AdminChallengeMsg message)
    {
        if (_pendingPasswordKey == null || _localPeerId == 0 ||
            message.Challenge.Length != AdminCrypto.CHALLENGE_BYTES)
        {
            ClearPendingSecrets();
            State.AddSystem("Invalid admin challenge.", isPrivate: true);
            return;
        }
        byte[] proof = AdminCrypto.ComputeProof(_pendingPasswordKey, _localPeerId,
            message.Challenge);
        _pendingAdminKey = AdminCrypto.DeriveSessionKey(_pendingPasswordKey, _localPeerId,
            message.Challenge);
        CryptographicOperations.ZeroMemory(_pendingPasswordKey);
        _pendingPasswordKey = null;
        new AdminProofMsg(proof).SendToServer();
        CryptographicOperations.ZeroMemory(proof);
    }

    private void OnAdminState(AdminStateMsg message)
    {
        bool wasAdmin = IsAdmin;
        if (message.IsAdmin && _pendingAdminKey != null)
        {
            ClearAdminKey();
            _adminKey = _pendingAdminKey;
            _pendingAdminKey = null;
            _nextAdminSequence = 1;
        }
        else if (!message.IsAdmin)
        {
            ClearAdminKey();
        }
        ClearPendingSecrets();
        State.AddSystem(message.Status, isPrivate: true);
        if (wasAdmin != IsAdmin)
            AdminChanged?.Invoke(IsAdmin);
    }

    private void ClearSecrets()
    {
        ClearPendingSecrets();
        ClearAdminKey();
        _nextAdminSequence = 1;
    }

    private void ClearPendingSecrets()
    {
        if (_pendingPasswordKey != null)
            CryptographicOperations.ZeroMemory(_pendingPasswordKey);
        if (_pendingAdminKey != null)
            CryptographicOperations.ZeroMemory(_pendingAdminKey);
        _pendingPasswordKey = null;
        _pendingAdminKey = null;
    }

    private void ClearAdminKey()
    {
        if (_adminKey != null)
            CryptographicOperations.ZeroMemory(_adminKey);
        _adminKey = null;
        _nextAdminSequence = 1;
    }
}
