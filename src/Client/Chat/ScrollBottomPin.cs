using Godot;

namespace Mortz.Client.Chat;

/// <summary>
/// Scrolls a ScrollContainer to the bottom reliably: a freshly added line has
/// no layout yet, so a same-frame scroll (even deferred) stops one row short.
/// Arming scrolls once now and again when the scrollbar's range changes.
/// </summary>
public sealed class ScrollBottomPin
{
    private readonly ScrollContainer _scroll;
    private bool _armed;

    public ScrollBottomPin(ScrollContainer scroll)
    {
        _scroll = scroll;
        _scroll.GetVScrollBar().Changed += OnRangeChanged;
    }

    public void Arm()
    {
        _armed = true;
        ScrollToBottom();
    }

    private void OnRangeChanged()
    {
        if (!_armed)
            return;
        _armed = false;
        ScrollToBottom();
    }

    private void ScrollToBottom() =>
        _scroll.ScrollVertical = checked((int)_scroll.GetVScrollBar().MaxValue);
}
