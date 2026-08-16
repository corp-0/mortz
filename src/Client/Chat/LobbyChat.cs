using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

namespace Mortz.Client.Chat;

/// <summary>View of <see cref="ClientChat"/>. The owning scene decides
/// visibility, size, and placement.</summary>
[Meta(typeof(IAutoNode))]
public partial class LobbyChat : PanelContainer
{
    [Export] private ScrollContainer _scroll = null!;
    [Export] private ChatFeed _feed = null!;
    [Export] private LineEdit _input = null!;

    private ScrollBottomPin _scrollPin = null!;

    [Dependency]
    private ClientChat Chat => this.DependOn<ClientChat>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _scrollPin = new ScrollBottomPin(_scroll);
        _input.TextSubmitted += OnTextSubmitted;
        _input.FocusEntered += OnFocusEntered;
        _input.FocusExited += OnFocusExited;
        VisibilityChanged += OnVisibilityChanged;
        _feed.LineAdded += OnLineAdded;
        _feed.Rebuilt += _scrollPin.Arm;
    }

    public void OnResolved() => _feed.Bind(Chat);

    public void OnExitTree() => ChatInputGuard.SetTyping(this, false);

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
        if (_input.IsInsideTree())
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
