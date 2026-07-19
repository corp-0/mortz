using Godot;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>View of <see cref="ClientChat"/>. The owning scene decides
/// visibility, size, and placement.</summary>
public partial class ChatPanel : PanelContainer
{
    [Export] private ClientChat _chat = null!;
    [Export] private ScrollContainer _scroll = null!;
    [Export] private VBoxContainer _lines = null!;
    [Export] private LineEdit _input = null!;

    private bool _subscribed;

    public override void _Ready()
    {
        _input.TextSubmitted += OnTextSubmitted;
        _input.FocusEntered += OnFocusEntered;
        _input.FocusExited += OnFocusExited;
        VisibilityChanged += OnVisibilityChanged;
        Subscribe();
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        ChatInputGuard.SetTyping(this, false);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() ||
            @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode is Key.Enter or Key.KpEnter && !_input.HasFocus())
        {
            _input.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape && _input.HasFocus())
        {
            _input.ReleaseFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnTextSubmitted(string text)
    {
        _input.Clear();
        _chat.Submit(text);
        _input.GrabFocus();
    }

    private void OnFocusEntered() => ChatInputGuard.SetTyping(this, true);
    private void OnFocusExited() => ChatInputGuard.SetTyping(this, false);

    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree())
            _input.ReleaseFocus();
    }

    private void OnEntryAdded(ChatEntry entry)
    {
        AddLine(entry, animate: true);
        CallDeferred(MethodName.ScrollToBottom);
    }

    private void OnCleared() => ClearLines();

    private void Rebuild()
    {
        ClearLines();
        foreach (ChatEntry entry in _chat.State.Entries)
        {
            AddLine(entry, animate: false);
        }
        CallDeferred(MethodName.ScrollToBottom);
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

    private void AddLine(ChatEntry entry, bool animate)
    {
        _lines.AddChild(
            entry.Kind == ChatEntryKind.ROLL && DiceRoll.TryParse(entry.Text, out int rolled)
                ? RollLine.Create(entry.SenderName, rolled, animate)
                : BuildTextLine(entry));
        while (_lines.GetChildCount() > NetConfig.MAX_CHAT_HISTORY)
        {
            _lines.GetChild(0).Free();
        }
    }

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
        };
    }

    private void ClearLines()
    {
        foreach (Node child in _lines.GetChildren())
        {
            _lines.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ScrollToBottom() =>
        _scroll.ScrollVertical = checked((int)_scroll.GetVScrollBar().MaxValue);
}
