using Mortz.Client.Admin;
using Mortz.Client.Chat.Commands;
using Mortz.Client.Session;
using Mortz.Core.Chat;
using Mortz.Core.Chat.Commands;
using Mortz.Core.Text;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Chat;

namespace Mortz.Client.Chat;

/// <summary>Chat history and commands for one connection.</summary>
public class ClientChat : IDisposable, IHandle<ChatMsg>
{
    private readonly ChatCommandRegistry<ClientCommandContext> _commands = new();
    private readonly List<ChatLine> _lines = [];
    private readonly ClientAdmin Admin;
    private readonly ISessionExit SessionExit;
    private readonly IClientSender Sender;
    private bool _closed;

    public ClientChat(ClientAdmin admin, ISessionExit sessionExit, IClientSender sender)
    {
        Admin = admin;
        SessionExit = sessionExit;
        Sender = sender;
        _commands.RegisterAssemblyCommands();
        Admin.StatusLine += OnAdminStatusLine;
    }

    public IReadOnlyList<ChatLine> Lines => _lines;
    public IEnumerable<ChatCommandMetadata> CommandCatalog => _commands.Commands;
    public event Action<ChatLine>? LineAdded;
    public event Action? Cleared;

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        Admin.StatusLine -= OnAdminStatusLine;
    }

    public void Handle(in ChatMsg message)
    {
        if (ChatProtocol.TryDecode(message, out ChatLine.Remote? line))
            Add(line);
    }

    public bool Submit(string? input)
    {
        if (_closed || string.IsNullOrWhiteSpace(input))
            return false;
        if (input.TrimStart().StartsWith('/'))
        {
            if (!_commands.TryParse(input.TrimStart(),
                    out ChatCommand<ClientCommandContext>? command, out string parseError))
            {
                AddPrivate(parseError);
                return false;
            }
            command!.Execute(new ClientCommandContext(this, Admin, SessionExit, Sender));
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
        new ChatSendMsg(text).SendToServer(Sender);
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

    private void OnAdminStatusLine(string line) => AddPrivate(line);
}
