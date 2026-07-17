using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Feed;
using Mortz.Core.Chat;
using Mortz.Core.Chat.Commands;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Chat;

/// <summary>Connected-session chat: history and command execution. Outlives
/// any view, so rearranging the UI keeps both. Also displays the kill feed
/// and admin status lines; those stay separate services with their own
/// subscribers.</summary>
public partial class ClientChat : SessionScopedNode
{
    [Export] private KillFeed _killFeed = null!;
    [Export] private ClientAdmin _admin = null!;

    private readonly ChatCommandRegistry<ClientCommandContext> _commands = new();

    public ClientChat() => _commands.RegisterAssemblyCommands();

    public ChatState State { get; } = new();
    public IEnumerable<ChatCommandMetadata> CommandCatalog => _commands.Commands;

    protected override void OnServiceReady()
    {
        ChatLineMsg.Received += OnChatLine;
        _killFeed.LineAdded += OnKillFeedLine;
        _admin.StatusLine += OnAdminStatusLine;
    }

    protected override void OnServiceExit()
    {
        ChatLineMsg.Received -= OnChatLine;
        _killFeed.LineAdded -= OnKillFeedLine;
        _admin.StatusLine -= OnAdminStatusLine;
        State.Clear();
    }

    protected override void OnSessionBoundary() => State.Clear();

    public bool Submit(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        if (input.TrimStart().StartsWith('/'))
        {
            if (!_commands.TryParse(input.TrimStart(),
                    out ChatCommand<ClientCommandContext>? command, out string parseError))
            {
                State.AddSystem(parseError, isPrivate: true);
                return false;
            }
            command!.Execute(new ClientCommandContext(this, _admin));
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

    private void OnChatLine(ChatLineMsg message)
    {
        if (!Enum.IsDefined(message.Kind) || string.IsNullOrWhiteSpace(message.Text))
            return;
        switch (message.Kind)
        {
            case ChatLineKind.PLAYER:
                State.Add(new ChatEntry(ChatEntryKind.PLAYER, message.SenderId,
                    message.SenderName, message.Text, ChatTextFormat.MARKDOWN));
                break;
            case ChatLineKind.ROLL when DiceRoll.TryParse(message.Text, out _):
                State.Add(new ChatEntry(ChatEntryKind.ROLL, message.SenderId,
                    message.SenderName, message.Text));
                break;
            case ChatLineKind.ROLL:
                break; // out-of-range roll value: drop like unknown kinds
            default:
                State.AddSystem(message.Text);
                break;
        }
    }

    private void OnKillFeedLine(string line) => State.AddSystem(line);

    private void OnAdminStatusLine(string line) => State.AddSystem(line, isPrivate: true);
}
