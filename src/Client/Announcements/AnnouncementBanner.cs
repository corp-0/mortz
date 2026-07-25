using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Roster;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Announcements;

/// <summary>On-screen text. Unlike the voice it suppresses nothing: every line
/// of a batch stacks, and the match-point warning stays up while the state
/// holds.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementBanner : Control
{
    private const float LINE_SECONDS = 2.6f;
    private const float LINE_FADE_SECONDS = 0.5f;
    private const int MAX_LINES = 4;
    private const int LINE_FONT_SIZE = 34;
    private const int MATCH_POINT_FONT_SIZE = 26;

    private static readonly Color _matchPointColor = new(1f, 0.82f, 0.2f);

    [Dependency]
    private AnnouncementDirector Director => this.DependOn<AnnouncementDirector>();

    [Dependency]
    private ClientRoster Roster => this.DependOn<ClientRoster>();

    private VBoxContainer _lines = null!;
    private Label _matchPoint = null!;
    private bool _subscribed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _matchPoint = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = _matchPointColor,
        };
        _matchPoint.AddThemeFontSizeOverride("font_size", MATCH_POINT_FONT_SIZE);
        _matchPoint.SetAnchorsPreset(LayoutPreset.TopWide);
        _matchPoint.OffsetTop = 36;
        AddChild(_matchPoint);

        _lines = new VBoxContainer();
        _lines.SetAnchorsPreset(LayoutPreset.TopWide);
        _lines.OffsetTop = 76;
        AddChild(_lines);
    }

    public void OnResolved()
    {
        Director.BatchReady += OnBatch;
        Director.MatchPointChanged += OnMatchPoint;
        _subscribed = true;
        // A late joiner walks in mid match point.
        if (Director.MatchPoint is { } active)
            OnMatchPoint(active);
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Director.BatchReady -= OnBatch;
        Director.MatchPointChanged -= OnMatchPoint;
        _subscribed = false;
    }

    private void OnBatch(IReadOnlyList<GameEventMsg> batch)
    {
        foreach (GameEventMsg e in batch)
        {
            if (AnnouncementText.Format(e, Roster.NameOf) is { } text)
                AddLine(text);
        }
    }

    private void OnMatchPoint(MatchPointMsg msg)
    {
        _matchPoint.Visible = msg.Active;
        if (msg.Active)
            _matchPoint.Text = AnnouncementText.MatchPoint(msg);
    }

    private void AddLine(string text)
    {
        Label line = new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        line.AddThemeFontSizeOverride("font_size", LINE_FONT_SIZE);
        _lines.AddChild(line);
        Tween tween = line.CreateTween();
        tween.TweenInterval(LINE_SECONDS);
        tween.TweenProperty(line, "modulate:a", 0f, LINE_FADE_SECONDS);
        tween.TweenCallback(Callable.From(line.QueueFree));

        int alive = 0;
        foreach (Node child in _lines.GetChildren())
        {
            if (!child.IsQueuedForDeletion())
                alive++;
        }
        if (alive <= MAX_LINES)
            return;
        foreach (Node child in _lines.GetChildren())
        {
            if (child.IsQueuedForDeletion())
                continue;
            child.QueueFree();
            break;
        }
    }
}
