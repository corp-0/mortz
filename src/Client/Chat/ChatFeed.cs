using Godot;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>Renders one <see cref="ClientChat"/>'s entries as child controls,
/// newest last.</summary>
public partial class ChatFeed : VBoxContainer
{
    private ClientChat _chat = null!;
    private bool _subscribed;

    /// <summary>Raised for the line a live entry appends, not during rebuilds.</summary>
    public event Action<Control>? LineAdded;

    public event Action? Rebuilt;

    public void Bind(ClientChat chat)
    {
        _chat = chat;
        Subscribe();
    }

    public override void _ExitTree() => Unsubscribe();

    // Not folded into the ?. call: a null-conditional skips argument
    // evaluation, and the line must exist even with no listeners.
    private void OnEntryAdded(ChatEntry entry)
    {
        Control line = AddLine(entry, animate: true);
        LineAdded?.Invoke(line);
    }

    private void OnCleared() => ClearLines();

    private void Rebuild()
    {
        ClearLines();
        foreach (ChatEntry entry in _chat.State.Entries)
        {
            AddLine(entry, animate: false);
        }
        Rebuilt?.Invoke();
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;
        _chat.State.EntryAdded += OnEntryAdded;
        _chat.State.Cleared += OnCleared;
        _subscribed = true;
        Rebuild();
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        _chat.State.EntryAdded -= OnEntryAdded;
        _chat.State.Cleared -= OnCleared;
        _subscribed = false;
    }

    private Control AddLine(ChatEntry entry, bool animate)
    {
        Control line =
            entry.Kind == ChatEntryKind.ROLL && DiceRoll.TryParse(entry.Text, out int rolled)
                ? RollLine.Create(entry.SenderName, rolled, animate)
                : BuildTextLine(entry);
        AddChild(line);
        while (GetChildCount() > NetConfig.MAX_CHAT_HISTORY)
        {
            GetChild(0).Free();
        }
        return line;
    }

    // Lines never take the mouse; the in-game overlay stays click-through.
    private static RichTextLabel BuildTextLine(ChatEntry entry)
    {
        RichText content = entry.Render();
        RichText text = entry.Kind switch
        {
            ChatEntryKind.PLAYER => new RichText().Bold().ApplyTo(entry.SenderName)
                .Add(": ").Add(content),
            ChatEntryKind.PRIVATE => new RichText().Italic().ApplyTo("* ").Add(content),
            _ => content,
        };
        return new RichTextLabel
        {
            Text = text.ToString(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = true,
            FitContent = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private void ClearLines()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }
}
