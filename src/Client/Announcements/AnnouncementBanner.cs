using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Ui;
using Mortz.Core.Match;
using Mortz.Core.Text;

namespace Mortz.Client.Announcements;

/// <summary>On-screen announcement text. Lines stack; the match-point warning
/// stays up while it holds.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementBanner : Control
{
    [Export] private float _lineSeconds = 2.6f;
    [Export] private float _lineFadeSeconds = 0.5f;
    [Export] private int _maxLines = 2;

    // Newest line sits on top; pushed-down lines dim.
    [Export] private float[] _tierBrightness = [1f, 0.6f];

    // Pixel sizes behind TextScale.
    [Export] private int[] _scalePixels = [16, 21, 28, 38, 50];

    [Export] private RichTextLabel _matchPoint = null!;
    [Export] private VBoxContainer _lines = null!;

    [Dependency]
    private IAnnouncementDirector Director => this.DependOn<IAnnouncementDirector>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Director.BatchReady += OnBatch;
        Director.MatchPointChanged += OnMatchPoint;
        // A late joiner walks in mid match point.
        if (Director.MatchPoint is { } active)
            OnMatchPoint(active);
    }

    public void OnExitTree()
    {
        Director.BatchReady -= OnBatch;
        Director.MatchPointChanged -= OnMatchPoint;
    }

    private void OnBatch(IReadOnlyList<Announcement> batch)
    {
        // Reversed so the highest-priority line lands on top.
        for (int i = batch.Count - 1; i >= 0; i--)
        {
            if (Compose(batch[i]) is { } line)
                AddLine(line);
        }
    }

    private void OnMatchPoint(MatchPointState mp)
    {
        _matchPoint.Visible = mp.Active;
        if (mp.Active)
        {
            _matchPoint.Text = new RichText(MatchPointLine(mp))
                .Wrap(new Style()
                    .Pulse(2f, RichTextColor.WHITE, 0.31f, -2f)
                    .Color(RichTextColor.BLOOD_RED)
                    .Center());
        }
    }

    private static string MatchPointLine(MatchPointState mp)
    {
        string kills = mp.Remaining <= 1 ? "one more kill" : $"{mp.Remaining} more kills";
        return mp.Leader == null ? $"{kills} wins!" : $"{mp.Leader} needs {kills}!";
    }

    /// <summary>Null for the kinds the banner stays quiet on.</summary>
    private RichText? Compose(Announcement a) => a.Kind switch
    {
        GameEventKind.FIRST_BLOOD => BuildFirstBlood(a),
        GameEventKind.HOLY_SHIT => BuildHolyShit(a),
        GameEventKind.MULTI_KILL => BuildMultiKill(a),
        GameEventKind.KILL_STREAK => BuildKillStreak(a),
        GameEventKind.SHUTDOWN => Player(a.Actor)
            .Add(" ")
            .Add("SHUT DOWN", Loud(TextScale.S)
                .Color(RichTextColor.ICE)
                .Pulse(2.5f, RichTextColor.WHITE, 0.38f, -2f))
            .Add(" ")
            .Add(Player(a.Victim))
            .Add("!")
            .Wrap(Base(TextScale.S)),
        GameEventKind.HUMILIATION => Player(a.Actor)
            .Add(" ")
            .Add("OWNED", Loud(TextScale.S))
            .Add(" ")
            .Add(Player(a.Victim))
            .Wrap(Base(TextScale.S).Color(RichTextColor.ORCHID).Tornado(6f, 4f)),
        GameEventKind.TEAM_WIPE => Player(a.Actor)
            .Add(" got a ")
            .Add("RAMPAGE", Loud(TextScale.S))
            .Add("!")
            .Wrap(Base(TextScale.S).Color(RichTextColor.FLAME)),
        _ => null,
    };

    private RichText BuildFirstBlood(Announcement a) => Player(a.Actor)
        .Add(" spilled ")
        .Add("FIRST BLOOD", Loud(TextScale.M)
            .Color(RichTextColor.BLOOD_RED)
            .Pulse(1.5f, RichTextColor.WHITE, 0.25f, -2f))
        .Add("!")
        .Wrap(Base(TextScale.S));

    private RichText BuildHolyShit(Announcement a)
    {
        MultiKillWording tier = Vocab.MultiKillTier(a.Magnitude);
        return new RichText()
            .Add("HOLY SHIT!", new Style()
                .Wave(24f, 6f)
                .Color(RichTextColor.BLACK)
                .FontSize(Pixels(TextScale.L)))
            .Add(" ")
            .Add(tier.Loud, MultiKillLoud(tier))
            .Add(" by ")
            .Add(Player(a.Actor))
            .Add("!")
            .Wrap(Base(TextScale.M));
    }

    private RichText BuildMultiKill(Announcement a)
    {
        MultiKillWording tier = Vocab.MultiKillTier(a.Magnitude);
        return Player(a.Actor)
            .Add($" {tier.Link} ")
            .Add(tier.Loud, MultiKillLoud(tier))
            .Add("!")
            .Wrap(Base(TextScale.M));
    }

    private RichText BuildKillStreak(Announcement a)
    {
        StreakWording tier = Vocab.StreakTier(a.Magnitude);
        return Player(a.Actor)
            .Add($" {tier.StreakVerb} ")
            .Add(tier.StreakName, StreakLoud(tier))
            .Add("!")
            .Wrap(Base(TextScale.XS));
    }

    // Heat ramps the loud word toward white-hot; effects join at the top tiers.
    private static readonly RichTextColor[] _multiKillHeatColors =
    [
        RichTextColor.GOLD, RichTextColor.AMBER, RichTextColor.EMBER,
        RichTextColor.VERMILION, RichTextColor.SCARLET, RichTextColor.WHITE_HOT,
    ];

    private static readonly RichTextColor[] _streakHeatColors =
    [
        RichTextColor.MOSS, RichTextColor.VENOM, RichTextColor.ACID,
        RichTextColor.ELECTRIC_LIME, RichTextColor.WHITE_HOT,
    ];

    private Style MultiKillLoud(MultiKillWording tier)
    {
        Style loud = Loud(TextScale.M).Color(_multiKillHeatColors[tier.Heat]);
        if (tier.Heat >= 4)
            loud.Shake(20f, 12);
        if (tier.Heat >= 5)
            loud.Pulse(2.5f, RichTextColor.SCARLET, 0.69f, -2f);
        else if (tier.Heat >= 3)
            loud.Pulse(2.5f, RichTextColor.WHITE, 0.38f, -2f);
        return loud;
    }

    private Style StreakLoud(StreakWording tier)
    {
        Style loud = Loud(TextScale.S).Color(_streakHeatColors[tier.Heat]);
        if (tier.Heat >= 3)
            loud.Shake(20f, 12);
        if (tier.Heat >= 4)
            loud.Pulse(2.5f, RichTextColor.ACID, 0.69f, -2f);
        else if (tier.Heat >= 2)
            loud.Pulse(2.5f, RichTextColor.WHITE, 0.38f, -2f);
        return loud;
    }

    private Style Base(TextScale scale) => new Style().FontSize(Pixels(scale));

    private Style Loud(TextScale baseScale) => new Style().Bold().FontSize(Pixels(baseScale + 2));

    private int Pixels(TextScale scale) =>
        _scalePixels[Math.Min((int)scale, _scalePixels.Length - 1)];

    private static RichText Player(Combatant p) => new RichText().Add(p.Name, NameStyle(p));

    private static Style NameStyle(Combatant p)
    {
        Style style = new Style().Bold();
        if (p.Team != 0)
            style.Color(TeamColors.For(p.Team));
        return style;
    }

    private void AddLine(RichText text)
    {
        RichTextLabel line = new()
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
            Text = text.Wrap(new Style().Center())
        };
        _lines.AddChild(line);
        _lines.MoveChild(line, 0);

        Tween tween = line.CreateTween();
        tween.TweenInterval(_lineSeconds);
        tween.TweenProperty(line, "modulate:a", 0f, _lineFadeSeconds);
        tween.TweenCallback(Callable.From(line.QueueFree));

        TrimOverflow();
        Restyle();
    }

    private void TrimOverflow()
    {
        Godot.Collections.Array<Node> children = _lines.GetChildren();
        if (children.Count(c => !c.IsQueuedForDeletion()) <= _maxLines)
            return;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].IsQueuedForDeletion())
                continue;
            children[i].QueueFree();
            break;
        }
    }

    private void Restyle()
    {
        int index = 0;
        foreach (Node child in _lines.GetChildren())
        {
            if (child.IsQueuedForDeletion())
                continue;
            var line = (RichTextLabel)child;
            int tier = Math.Min(index, _tierBrightness.Length - 1);
            float brightness = _tierBrightness[tier];
            line.SelfModulate = new Color(brightness, brightness, brightness);
            index++;
        }
    }
}
