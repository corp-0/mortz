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
            if (Compose(a) is RichText line)
                Chat.AddSystem(line);
        }
    }

    /// <summary>One line on entering; the banner keeps the standing warning.</summary>
    private void OnMatchPoint(MatchPointState? state)
    {
        if (state is not MatchPointState held)
            return;
        string kills = held.Remaining <= 1 ? "one more kill" : $"{held.Remaining} more kills";
        RichText line = held.Leader is MatchPointLeader leader
            ? new RichText().Add(leader.Name, NameStyle(leader.Team)).Add(" needs ")
                .Add(kills, new Style().Bold()).Add("!")
            : new RichText().Add(kills, new Style().Bold()).Add(" wins!");
        Chat.AddSystem(line);
    }

    private RichText? Compose(Announcement a) => a.Kind switch
    {
        GameEventKind.REGULAR_KILL =>
            Player(a.Actor)
            .Add(" killed ")
            .Add(Player(a.Victim)),

        GameEventKind.TEAM_KILL => Player(a.Actor)
            .Add(" team-killed ").Add(Player(a.Victim)),

        GameEventKind.FIRST_BLOOD =>
            Player(a.Actor)
            .Add(" spilled ")
            .Add("first blood", new Style().Bold().Color(RichTextColor.BLOOD_RED))
            .Add("!"),

        GameEventKind.HUMILIATION =>
            Player(a.Actor)
            .Add(" ")
            .Add("OWNED", new Style().Bold())
            .Add(" ")
            .Add(Player(a.Victim)),

        GameEventKind.SHUTDOWN =>
            Player(a.Actor)
            .Add(" ended ")
            .Add(Player(a.Victim))
            .Add("'s killstreak!"),

        GameEventKind.HOLY_SHIT => new RichText()
            .Add("Holy shit!", new Style().Bold())
            .Add(" ")
            .Add(MultiKill(a)),

        GameEventKind.MULTI_KILL => MultiKill(a),

        GameEventKind.KILL_STREAK => Streak(a),

        GameEventKind.TEAM_WIPE =>
            Player(a.Actor)
            .Add(" ")
            .Add("RAMPAGED", new Style().Bold())
            .Add(" the enemy team!"),

        GameEventKind.REVENGE =>
            Player(a.Actor)
            .Add(" got revenge on ")
            .Add(Player(a.Victim)),

        GameEventKind.SUICIDE =>
            Player(a.Actor)
            .Add(" " + Vocab.SuicideQuip(a.Cause, _picker)),

        _ => null,
    };

    private static RichText MultiKill(Announcement a)
    {
        MultiKillWording tier = Vocab.MultiKillTier(a.Magnitude);
        return
            Player(a.Actor)
            .Add($" {tier.Link} ")
            .Add(tier.Loud, new Style().Bold())
            .Add("!");
    }

    private static RichText Streak(Announcement a)
    {
        StreakWording tier = Vocab.StreakTier(a.Magnitude);
        return
            Player(a.Actor)
            .Add($" {tier.Weak} ")
            .Add(tier.Strong, new Style().Bold())
            .Add("!");
    }

    private static RichText Player(Combatant p) => new RichText().Add(p.Name, NameStyle(p));

    private static Style NameStyle(Combatant p) => NameStyle(p.Team);

    private static Style NameStyle(Team? team)
    {
        Style style = new Style().Bold();
        if (team is Team assigned)
            style.Color(TeamColors.For(assigned));
        return style;
    }
}
