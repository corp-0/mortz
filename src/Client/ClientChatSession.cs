using System.Security.Cryptography;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Core.Text;

namespace Mortz.Client;

public abstract record ClientChatCommand : IChatCommand
{
    internal abstract void Execute(ClientChatSession session);
}

public sealed record HelpChatCommand : ClientChatCommand
{
    internal override void Execute(ClientChatSession session) => session.Execute(this);
}

public sealed record AuthenticateAdminChatCommand(string Password) : ClientChatCommand,
    ISensitiveChatCommand
{
    internal override void Execute(ClientChatSession session) => session.Execute(this);
}

internal sealed class HelpChatCommandDefinition :
    ChatCommandDefinition<ClientChatCommand, HelpChatCommand>
{
    public HelpChatCommandDefinition() : base(new ChatCommandName("help"), "/help",
        "List chat commands.")
    {
    }

    public override bool TryBind(IReadOnlyList<string> arguments, out HelpChatCommand? command,
        out string error)
    {
        command = arguments.Count == 0 ? new HelpChatCommand() : null;
        error = command == null ? "Usage: /help" : "";
        return command != null;
    }
}

internal sealed class AuthenticateAdminChatCommandDefinition :
    ChatCommandDefinition<ClientChatCommand, AuthenticateAdminChatCommand>
{
    public AuthenticateAdminChatCommandDefinition() : base(new ChatCommandName("admin"),
        "/admin <password>", "Authenticate as a lobby admin.")
    {
    }

    public override bool TryBind(IReadOnlyList<string> arguments,
        out AuthenticateAdminChatCommand? command, out string error)
    {
        command = arguments.Count == 1 && arguments[0].Length > 0
            ? new AuthenticateAdminChatCommand(arguments[0])
            : null;
        error = command == null ? "Usage: /admin <password>" : "";
        return command != null;
    }
}

/// <summary>Connected-session chat and command state. Outlives any view, so
/// rearranging the UI keeps history, command state, and admin authority.</summary>
public sealed class ClientChatSession : IDisposable
{
    private readonly ChatCommandRegistry<ClientChatCommand> _commands = new();
    private readonly Dictionary<long, string> _names = new();
    private byte[]? _pendingPasswordKey;
    private byte[]? _pendingAdminKey;
    private byte[]? _adminKey;
    private long _localPeerId;
    private ulong _nextAdminSequence = 1;
    private bool _subscribed;

    public ClientChatSession()
    {
        _commands.Register(new HelpChatCommandDefinition());
        _commands.Register(new AuthenticateAdminChatCommandDefinition());
    }

    public ChatState State { get; } = new();
    public bool IsAdmin => _adminKey != null;
    public IEnumerable<ChatCommandMetadata> CommandCatalog =>
        _commands.Definitions.Select(definition => definition.Metadata);
    public event Action<bool>? AdminChanged;

    public void Subscribe()
    {
        if (_subscribed)
            return;
        ChatLineMsg.Received += OnChatLine;
        AdminChallengeMsg.Received += OnAdminChallenge;
        AdminStateMsg.Received += OnAdminState;
        LobbyStateMsg.Received += OnLobbyState;
        RosterMsg.Received += OnRoster;
        EliminationMsg.Received += OnElimination;
        MatchEndMsg.Received += OnMatchEnd;
        _subscribed = true;
    }

    public void Begin()
    {
        ClearSecrets();
        _localPeerId = 0;
        _names.Clear();
        State.Clear();
    }

    public void SetLocalPeerId(long peerId) => _localPeerId = peerId;

    public bool Submit(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        if (input.TrimStart().StartsWith('/'))
        {
            if (!_commands.TryParse(input.TrimStart(), out ClientChatCommand? command,
                    out string parseError))
            {
                State.AddSystem(parseError, isPrivate: true);
                return false;
            }
            command!.Execute(this);
            return true;
        }

        if (!ChatTextSanitizer.TrySanitize(input, out string text, out ChatRejectReason reason))
        {
            string error = reason == ChatRejectReason.TooLong
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
        _names.Clear();
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
            LobbyStateMsg.Received -= OnLobbyState;
            RosterMsg.Received -= OnRoster;
            EliminationMsg.Received -= OnElimination;
            MatchEndMsg.Received -= OnMatchEnd;
            _subscribed = false;
        }
        End();
    }

    internal void Execute(HelpChatCommand _)
    {
        foreach (ChatCommandMetadata metadata in CommandCatalog)
        {
            RichText line = new RichText()
                .Bold().ApplyTo(metadata.Usage)
                .Add(" - ").Add(metadata.Description);
            State.AddSystem(line, isPrivate: true);
        }
    }

    internal void Execute(AuthenticateAdminChatCommand command)
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
        _pendingPasswordKey = AdminCrypto.DerivePasswordKey(command.Password);
        new AdminAuthRequestMsg().SendToServer();
        State.AddSystem("Requesting admin challenge...", isPrivate: true);
    }

    private void OnChatLine(ChatLineMsg message)
    {
        if (!Enum.IsDefined(message.Kind) || string.IsNullOrWhiteSpace(message.Text))
            return;
        if (message.Kind == ChatLineKind.PLAYER)
            State.Add(new ChatEntry(ChatEntryKind.Player, message.SenderId,
                message.SenderName, message.Text, ChatTextFormat.Markdown));
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

    private void OnLobbyState(LobbyStateMsg message) =>
        UpdateNames(message.PeerIds, message.Names);

    private void OnRoster(RosterMsg message) => UpdateNames(message.PeerIds, message.Names);

    private void OnElimination(EliminationMsg message) =>
        State.AddSystem(EliminationText.Format(message, Name));

    private void OnMatchEnd(MatchEndMsg message)
    {
        string winner = message.ByTeam ? $"Team {message.WinnerId}" : Name(message.WinnerId);
        State.AddSystem($"{winner} wins!");
    }

    private void UpdateNames(long[] peerIds, string[] names)
    {
        _names.Clear();
        int count = Math.Min(peerIds.Length, names.Length);
        for (int i = 0; i < count; i++)
            _names[peerIds[i]] = names[i];
    }

    private string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";

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
