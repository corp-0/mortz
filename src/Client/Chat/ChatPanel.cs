using Godot;

namespace Mortz.Client.Chat;

/// <summary>View of <see cref="ClientChat"/>. The owning scene decides
/// visibility, size, and placement.</summary>
public partial class ChatPanel : PanelContainer
{
    [Export] private ClientChat _chat = null!;
    [Export] private ScrollContainer _scroll = null!;
    [Export] private ChatFeed _feed = null!;
    [Export] private LineEdit _input = null!;

    private ScrollBottomPin _scrollPin = null!;

    public override void _Ready()
    {
        _scrollPin = new ScrollBottomPin(_scroll);
        _input.TextSubmitted += OnTextSubmitted;
        _input.FocusEntered += OnFocusEntered;
        _input.FocusExited += OnFocusExited;
        VisibilityChanged += OnVisibilityChanged;
        _feed.LineAdded += OnLineAdded;
        _feed.Rebuilt += _scrollPin.Arm;
        _feed.Bind(_chat);
    }

    public override void _ExitTree() => ChatInputGuard.SetTyping(this, false);

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

    private void OnLineAdded(Control line) => _scrollPin.Arm();
}
