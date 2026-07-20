using Godot;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Chat;

/// <summary>
/// In-game chat overlay. Closed, it is invisible and mouse-transparent except
/// for recent feed lines, which fade away after a few seconds. ENTER, T, or /
/// opens the full history and input; ENTER sends and closes, ESC closes and
/// keeps the draft.
/// </summary>
public partial class GameChat : Control
{
    private const float LINE_LIFETIME = 7f; // s at full alpha after arrival
    private const float FADE_TIME = 1f; // s from full alpha to gone
    private const int CLOSED_MAX_LINES = 8;

    private static readonly StringName _bornMeta = "chat_born_msec";

    [Export] private ClientChat _chat = null!;
    [Export] private Panel _background = null!;
    [Export] private ScrollContainer _scroll = null!;
    [Export] private ChatFeed _feed = null!;
    [Export] private LineEdit _input = null!;

    private ScrollBottomPin _scrollPin = null!;
    private bool _open;

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
        Close();
    }

    public override void _ExitTree()
    {
        if (_open)
            new TypingMsg(false).SendToServer();
        ChatInputGuard.SetTyping(this, false);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() ||
            @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (!_open)
        {
            // T and / cannot leak into the input: gui dispatch for this event
            // already ran before the unhandled stage.
            switch (key.Keycode)
            {
                case Key.Enter or Key.KpEnter or Key.T:
                    Open();
                    break;
                case Key.Slash:
                    Open();
                    if (_input.Text.Length == 0)
                    {
                        _input.Text = "/";
                        _input.CaretColumn = 1;
                    }
                    break;
                default:
                    return;
            }
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape)
        {
            // Handled here so ESC cannot also reach the pause menu.
            Close();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter && !_input.HasFocus())
        {
            _input.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_open)
            return;
        ulong now = Time.GetTicksMsec();
        int shown = 0;
        for (int i = _feed.GetChildCount() - 1; i >= 0; i--)
        {
            if (_feed.GetChild(i) is not Control line)
                continue;
            float alpha = shown >= CLOSED_MAX_LINES ? 0f : ClosedAlpha(AgeSeconds(line, now));
            if (alpha > 0f)
                shown++;
            line.Modulate = new Color(1f, 1f, 1f, alpha);
        }
    }

    internal static float ClosedAlpha(float ageSeconds)
    {
        if (ageSeconds <= LINE_LIFETIME)
            return 1f;
        return Mathf.Clamp(1f - (ageSeconds - LINE_LIFETIME) / FADE_TIME, 0f, 1f);
    }

    // Rebuilt history has no birth stamp and renders already expired.
    private static float AgeSeconds(Control line, ulong nowMsec) =>
        line.HasMeta(_bornMeta)
            ? (nowMsec - (ulong)line.GetMeta(_bornMeta)) / 1000f
            : float.MaxValue;

    private void Open()
    {
        _open = true;
        _background.Visible = true;
        _input.Visible = true;
        _scroll.MouseFilter = MouseFilterEnum.Stop;
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        foreach (Node child in _feed.GetChildren())
        {
            if (child is Control line)
                line.Modulate = Colors.White;
        }
        _input.GrabFocus();
        _scrollPin.Arm();
    }

    /// <summary>The draft survives; only a send clears it.</summary>
    private void Close()
    {
        _open = false;
        _input.ReleaseFocus();
        _background.Visible = false;
        _input.Visible = false;
        _scroll.MouseFilter = MouseFilterEnum.Ignore;
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        _scrollPin.Arm();
    }

    private void OnTextSubmitted(string text)
    {
        _input.Clear();
        _chat.Submit(text);
        Close();
    }

    private void OnFocusEntered()
    {
        ChatInputGuard.SetTyping(this, true);
        new TypingMsg(true).SendToServer();
    }

    private void OnFocusExited()
    {
        ChatInputGuard.SetTyping(this, false);
        new TypingMsg(false).SendToServer();
    }

    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree() && _open)
            Close();
    }

    private void OnLineAdded(Control line)
    {
        line.SetMeta(_bornMeta, Time.GetTicksMsec());
        _scrollPin.Arm();
    }
}
