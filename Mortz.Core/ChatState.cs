using Mortz.Core.Text;

namespace Mortz.Core;

public enum ChatEntryKind : byte
{
    Player,
    System,
    Private,
}

public enum ChatTextFormat : byte
{
    Plain,
    Markdown,
    RichText,
}

public readonly record struct ChatEntry(
    ChatEntryKind Kind,
    long SenderId,
    string SenderName,
    string Text,
    ChatTextFormat TextFormat = ChatTextFormat.Plain)
{
    public RichText Render() => TextFormat switch
    {
        ChatTextFormat.Markdown => ChatMarkdown.Render(Text),
        ChatTextFormat.RichText => RichText.FromTrustedBbCode(Text),
        _ => new RichText(Text),
    };
}

/// <summary>Persistent connected-session chat state, independent from any visual layout.</summary>
public sealed class ChatState
{
    private readonly int _capacity;
    private readonly List<ChatEntry> _entries = [];

    public ChatState(int capacity = NetConfig.MAX_CHAT_HISTORY)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public IReadOnlyList<ChatEntry> Entries => _entries;
    public event Action<ChatEntry>? EntryAdded;
    public event Action? Cleared;

    public void Add(ChatEntry entry)
    {
        if (_entries.Count == _capacity)
            _entries.RemoveAt(0);
        _entries.Add(entry);
        EntryAdded?.Invoke(entry);
    }

    public void AddSystem(string text, bool isPrivate = false) =>
        Add(new ChatEntry(isPrivate ? ChatEntryKind.Private : ChatEntryKind.System,
            0, isPrivate ? "" : "Server", text));

    public void AddSystem(RichText text, bool isPrivate = false) =>
        Add(new ChatEntry(isPrivate ? ChatEntryKind.Private : ChatEntryKind.System,
            0, isPrivate ? "" : "Server", text, ChatTextFormat.RichText));

    public void Clear()
    {
        _entries.Clear();
        Cleared?.Invoke();
    }
}
