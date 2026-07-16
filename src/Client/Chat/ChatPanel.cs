using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>View of <see cref="IClientChat"/>. The owning scene decides
/// visibility, size, and placement.</summary>
[Meta(typeof(IAutoNode))]
public partial class ChatPanel : PanelContainer
{
    [Export] private ScrollContainer _scroll = null!;
    [Export] private VBoxContainer _lines = null!;
    [Export] private LineEdit _input = null!;

    private bool _subscribed;

    [Dependency]
    public IClientChat Chat => this.DependOn<IClientChat>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _input.TextSubmitted += OnTextSubmitted;
        _input.FocusEntered += OnFocusEntered;
        _input.FocusExited += OnFocusExited;
        VisibilityChanged += OnVisibilityChanged;
    }

    public void OnResolved() => Subscribe();

    public void OnExitTree()
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
        Chat.Submit(text);
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
        AddLine(entry);
        CallDeferred(MethodName.ScrollToBottom);
    }

    private void OnCleared() => ClearLines();

    private void Rebuild()
    {
        ClearLines();
        foreach (ChatEntry entry in Chat.State.Entries)
            AddLine(entry);
        CallDeferred(MethodName.ScrollToBottom);
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;
        Chat.State.EntryAdded += OnEntryAdded;
        Chat.State.Cleared += OnCleared;
        _subscribed = true;
        Rebuild();
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        Chat.State.EntryAdded -= OnEntryAdded;
        Chat.State.Cleared -= OnCleared;
        _subscribed = false;
    }

    private void AddLine(ChatEntry entry)
    {
        RichText content = entry.Render();
        RichText text = entry.Kind switch
        {
            ChatEntryKind.PLAYER => new RichText().Bold().ApplyTo(entry.SenderName)
                .Add(": ").Add(content),
            ChatEntryKind.PRIVATE => new RichText().Italic().ApplyTo("* ").Add(content),
            _ => content,
        };
        _lines.AddChild(new RichTextLabel
        {
            Text = text.ToString(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = true,
            FitContent = true,
        });
        while (_lines.GetChildCount() > NetConfig.MAX_CHAT_HISTORY)
            _lines.GetChild(0).Free();
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
