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
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>Chat history and command execution, one instance per screen
/// (lobby, game view) so history dies with its screen.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientChat : Node
{
    private readonly ChatCommandRegistry<ClientCommandContext> _commands = new();
    private readonly List<ChatLine> _lines = [];
    private bool _subscribed;

    public ClientChat() => _commands.RegisterAssemblyCommands();

    public IReadOnlyList<ChatLine> Lines => _lines;
    public IEnumerable<ChatCommandMetadata> CommandCatalog => _commands.Commands;
    public event Action<ChatLine>? LineAdded;
    public event Action? Cleared;

    [Dependency]
    private ClientAdmin Admin => this.DependOn<ClientAdmin>();

    [Dependency]
    private ISessionExit SessionExit => this.DependOn<ISessionExit>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        ChatProtocol.Received += OnChatLine;
        Admin.StatusLine += OnAdminStatusLine;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        ChatProtocol.Received -= OnChatLine;
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
                AddPrivate(parseError);
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
            AddPrivate(error);
            return false;
        }
        new ChatSendMsg(text).SendToServer();
        return true;
    }

    public void Add(ChatLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (_lines.Count == NetConfig.MAX_CHAT_HISTORY)
            _lines.RemoveAt(0);
        _lines.Add(line);
        LineAdded?.Invoke(line);
    }

    public void AddSystem(string text) => Add(new ChatLine.System(text));

    public void AddSystem(RichText text) => Add(new ChatLine.System(text));

    public void AddPrivate(string text) => Add(new ChatLine.Private(text));

    public void AddPrivate(RichText text) => Add(new ChatLine.Private(text));

    public void Clear()
    {
        _lines.Clear();
        Cleared?.Invoke();
    }

    private void OnChatLine(ChatLine.Remote line) => Add(line);

    private void OnAdminStatusLine(string line) => AddPrivate(line);
}
