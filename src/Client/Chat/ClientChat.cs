using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Chat.Commands;
using Mortz.Client.Session;
using Mortz.Core.Chat;
using Mortz.Core.Chat.Commands;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Chat;

/// <summary>Chat history and command execution, one instance per screen
/// (lobby, game view) so history dies with its screen.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientChat : Node
{
    private readonly ChatCommandRegistry<ClientCommandContext> _commands = new();
    private bool _subscribed;

    public ClientChat() => _commands.RegisterAssemblyCommands();

    public ChatState State { get; } = new();
    public IEnumerable<ChatCommandMetadata> CommandCatalog => _commands.Commands;

    [Dependency]
    private ClientAdmin Admin => this.DependOn<ClientAdmin>();

    [Dependency]
    private ISessionExit SessionExit => this.DependOn<ISessionExit>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        ChatLineMsg.Received += OnChatLine;
        Admin.StatusLine += OnAdminStatusLine;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        ChatLineMsg.Received -= OnChatLine;
        Admin.StatusLine -= OnAdminStatusLine;
        _subscribed = false;
    }

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
            command!.Execute(new ClientCommandContext(this, Admin, SessionExit));
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

    private void OnAdminStatusLine(string line) => State.AddSystem(line, isPrivate: true);
}
