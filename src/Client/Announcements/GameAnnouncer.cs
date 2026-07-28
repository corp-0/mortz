using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Core.Match;
using Mortz.Net;

namespace Mortz.Client.Announcements;

/// <summary>The voice. Bursts swallow the streak line, humiliation only plays
/// when nothing bigger happened, suicide mockery is local only.</summary>
[Meta(typeof(IAutoNode))]
public partial class GameAnnouncer : Node
{
    internal enum Cue
    {
        FIRST_BLOOD,
        HUMILIATION,
        SHUTDOWN,
        HOLY_SHIT,
        DOUBLE_KILL,
        TRIPLE_KILL,
        OVERKILL,
        ULTRA_KILL,
        MASSACRE,
        CARNAGE,
        BLOODLUST,
        PUNISHMENT,
        DOMINATING,
        MACHINE_GOD,
        PSYCHO,
        TEAM_WIPE,
        SUICIDE,
    }

    /// <summary>Consecutive suicides before the announcer bothers to mock.</summary>
    internal const int SUICIDE_MOCK_COUNT = 3;

    [Dependency]
    private IAnnouncementDirector Director => this.DependOn<IAnnouncementDirector>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ISfx Sfx => this.DependOn<ISfx>();

    private readonly AnnouncementQueue _queue = new();
    private readonly VariantPicker _picker = new();
    private double _clock;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => Director.BatchReady += OnBatch;

    public void OnExitTree() => Director.BatchReady -= OnBatch;

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_queue.Next(_clock) is { } cue)
        {
            SoundEffect?[] variants = Sounds(cue);
            Sfx.Play(variants[_picker.Next(cue, variants.Length)]);
        }
    }

    private void OnBatch(IReadOnlyList<Announcement> batch)
    {
        foreach (Cue cue in Plan(batch, Network.LocalPeerId))
        {
            _queue.Push(cue, _clock);
        }
    }

    /// <summary>The lines to speak for one priority-ordered batch.</summary>
    internal static List<Cue> Plan(IReadOnlyList<Announcement> batch, long localId)
    {
        List<Cue> cues = new();
        bool hasBurst = batch.Any(a =>
            a.Kind is GameEventKind.HOLY_SHIT or GameEventKind.MULTI_KILL);
        bool humiliation = false;
        foreach (Announcement a in batch)
        {
            switch (a.Kind)
            {
                case GameEventKind.HOLY_SHIT:
                    cues.Add(Cue.HOLY_SHIT);
                    cues.Add(MultiKillCue(a.Magnitude));
                    break;
                case GameEventKind.FIRST_BLOOD:
                    cues.Add(Cue.FIRST_BLOOD);
                    break;
                case GameEventKind.SHUTDOWN:
                    cues.Add(Cue.SHUTDOWN);
                    break;
                case GameEventKind.TEAM_WIPE:
                    cues.Add(Cue.TEAM_WIPE);
                    break;
                case GameEventKind.MULTI_KILL:
                    cues.Add(MultiKillCue(a.Magnitude));
                    break;
                case GameEventKind.KILL_STREAK when !hasBurst:
                    cues.Add(StreakCue(a.Magnitude));
                    break;
                case GameEventKind.SUICIDE
                    when a.Actor.Id == localId && a.Magnitude >= SUICIDE_MOCK_COUNT:
                    cues.Add(Cue.SUICIDE);
                    break;
                case GameEventKind.HUMILIATION:
                    humiliation = true;
                    break;
            }
        }
        if (cues.Count == 0 && humiliation)
            cues.Add(Cue.HUMILIATION);
        return cues;
    }

    private static Cue MultiKillCue(byte magnitude) => magnitude switch
    {
        <= 2 => Cue.DOUBLE_KILL,
        3 => Cue.TRIPLE_KILL,
        4 => Cue.OVERKILL,
        5 => Cue.ULTRA_KILL,
        6 => Cue.MASSACRE,
        _ => Cue.CARNAGE,
    };

    private static Cue StreakCue(byte magnitude) =>
        GameEventJudge.StreakAnnouncementOrdinal(magnitude) switch
        {
            0 => Cue.BLOODLUST,
            1 => Cue.PUNISHMENT,
            2 => Cue.DOMINATING,
            3 => Cue.MACHINE_GOD,
            _ => Cue.PSYCHO,
        };

    /// <summary>1..n variants per cue; most have one for now.</summary>
    private SoundEffect?[] Sounds(Cue cue) => cue switch
    {
        Cue.FIRST_BLOOD => [Sfx.Sounds.FirstBlood],
        Cue.HUMILIATION => [Sfx.Sounds.Owned],
        Cue.SHUTDOWN => [Sfx.Sounds.Shutdown],
        Cue.HOLY_SHIT => [Sfx.Sounds.HolyShit],
        Cue.DOUBLE_KILL => [Sfx.Sounds.DoubleKill],
        Cue.TRIPLE_KILL => [Sfx.Sounds.TripleKill],
        Cue.OVERKILL => [Sfx.Sounds.Overkill],
        Cue.ULTRA_KILL => [Sfx.Sounds.UltraKill],
        Cue.MASSACRE => [Sfx.Sounds.Massacre],
        Cue.CARNAGE => [Sfx.Sounds.Carnage],
        Cue.BLOODLUST => [Sfx.Sounds.Bloodlust],
        Cue.PUNISHMENT => [Sfx.Sounds.Punishment],
        Cue.DOMINATING => [Sfx.Sounds.Dominating],
        Cue.MACHINE_GOD => [Sfx.Sounds.MachineGod],
        Cue.PSYCHO => [Sfx.Sounds.Psycho],
        Cue.TEAM_WIPE => [Sfx.Sounds.TeamWipe],
        Cue.SUICIDE => [Sfx.Sounds.SuicideMock],
        _ => [null],
    };
}
