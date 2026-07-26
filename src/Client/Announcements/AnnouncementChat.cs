using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Chat;
using Mortz.Client.Ui;
using Mortz.Core.Match;
using Mortz.Core.Text;

namespace Mortz.Client.Announcements;

/// <summary>Prints announcement lines into the in-game chat.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementChat : Node
{
    [Dependency]
    private IAnnouncementDirector Director => this.DependOn<IAnnouncementDirector>();

    [Dependency]
    private ClientChat Chat => this.DependOn<ClientChat>();

    private readonly VariantPicker _picker = new();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Director.BatchReady += OnBatch;
        Director.MatchPointChanged += OnMatchPoint;
    }

    public void OnExitTree()
    {
        Director.BatchReady -= OnBatch;
        Director.MatchPointChanged -= OnMatchPoint;
    }

    private void OnBatch(IReadOnlyList<Announcement> batch)
    {
        foreach (Announcement a in batch)
        {
            if (Compose(a) is { } line)
                Chat.State.AddSystem(line);
        }
    }

    /// <summary>One line on entering; the banner keeps the standing warning.</summary>
    private void OnMatchPoint(MatchPointState mp)
    {
        if (!mp.Active)
            return;
        string kills = mp.Remaining <= 1 ? "one more kill" : $"{mp.Remaining} more kills";
        RichText line = mp.Leader == null
            ? new RichText().Add(kills, new Style().Bold()).Add(" wins!")
            : new RichText().Add(mp.Leader, new Style().Bold()).Add(" needs ")
                .Add(kills, new Style().Bold()).Add("!");
        Chat.State.AddSystem(line);
    }

    private RichText? Compose(Announcement a) => a.Kind switch
    {
        GameEventKind.FIRST_BLOOD => new RichText()
            .Add(Player(a.Actor)).Add(" spilled ")
            .Add("first blood", new Style().Bold().Color(RichTextColor.BLOOD_RED))
            .Add("!"),

        GameEventKind.HUMILIATION => new RichText()
            .Add(Player(a.Actor))
            .Add(" ").Add("OWNED", new Style().Bold())
            .Add(" ")
            .Add(Player(a.Victim)),

        GameEventKind.SHUTDOWN => new RichText()
            .Add(Player(a.Actor)).Add(" ended ").Add(Player(a.Victim)).Add("'s killstreak!"),

        GameEventKind.HOLY_SHIT => new RichText()
            .Add("Holy shit!", new Style().Bold()).Add(" ").Add(MultiKill(a)),

        GameEventKind.MULTI_KILL => MultiKill(a),

        GameEventKind.KILL_STREAK => Streak(a),

        GameEventKind.TEAM_WIPE => new RichText()
            .Add(Player(a.Actor)).Add(" ").Add("RAMPAGED", new Style().Bold())
            .Add(" the enemy team!"),

        GameEventKind.REVENGE => new RichText()
            .Add(Player(a.Actor)).Add(" got revenge on ").Add(Player(a.Victim)),

        GameEventKind.SUICIDE => new RichText()
            .Add(Player(a.Actor)).Add(" " + Vocab.SuicideQuip(a.Cause, _picker)),
        _ => null,
    };

    private static RichText MultiKill(Announcement a)
    {
        MultiKillWording tier = Vocab.MultiKillTier(a.Magnitude);
        return new RichText()
            .Add(Player(a.Actor)).Add($" {tier.Link} ").Add(tier.Loud, new Style().Bold()).Add("!");
    }

    private static RichText Streak(Announcement a)
    {
        StreakWording tier = Vocab.StreakTier(a.Magnitude);
        return new RichText()
            .Add(Player(a.Actor)).Add($" {tier.Weak} ").Add(tier.Strong, new Style().Bold()).Add("!");
    }

    private static RichText Player(Combatant p) => new RichText().Add(p.Name, NameStyle(p));

    private static Style NameStyle(Combatant p)
    {
        Style style = new Style().Bold();
        if (p.Team != 0)
            style.Color(TeamColors.For(p.Team));
        return style;
    }
}
