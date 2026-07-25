using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Announcements;

/// <summary>The voice. Burst lines swallow the streak line, humiliation only
/// plays for the two involved and only when nothing bigger happened.</summary>
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
        MULTI_KILL,
        KILL_STREAK,
    }

    [Dependency]
    private AnnouncementDirector Director => this.DependOn<AnnouncementDirector>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    private readonly AnnouncementQueue _queue = new();
    private double _clock;
    private bool _subscribed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Director.BatchReady += OnBatch;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Director.BatchReady -= OnBatch;
        _subscribed = false;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_queue.Next(_clock) is { } cue)
            Sfx.Play(Sound(cue));
    }

    private void OnBatch(IReadOnlyList<GameEventMsg> batch)
    {
        foreach (Cue cue in Plan(batch, Network.LocalPeerId))
        {
            _queue.Push(cue, _clock);
        }
    }

    /// <summary>The lines to speak for one priority-ordered batch, in order.</summary>
    internal static List<Cue> Plan(IReadOnlyList<GameEventMsg> batch, long localId)
    {
        List<Cue> cues = new();
        bool hasBurst = batch.Any(e =>
            e.Kind is GameEventKind.HOLY_SHIT or GameEventKind.MULTI_KILL);
        GameEventMsg? humiliation = null;
        foreach (GameEventMsg e in batch)
        {
            switch (e.Kind)
            {
                case GameEventKind.HOLY_SHIT:
                    cues.Add(Cue.HOLY_SHIT);
                    break;
                case GameEventKind.FIRST_BLOOD:
                    cues.Add(Cue.FIRST_BLOOD);
                    break;
                case GameEventKind.SHUTDOWN:
                    cues.Add(Cue.SHUTDOWN);
                    break;
                case GameEventKind.MULTI_KILL:
                    cues.Add(MultiKillCue(e.Magnitude));
                    break;
                case GameEventKind.KILL_STREAK when !hasBurst:
                    cues.Add(Cue.KILL_STREAK);
                    break;
                case GameEventKind.HUMILIATION
                    when e.ActorId == localId || e.VictimId == localId:
                    humiliation = e;
                    break;
            }
        }
        if (cues.Count == 0 && humiliation != null)
            cues.Add(Cue.HUMILIATION);
        return cues;
    }

    private static Cue MultiKillCue(byte magnitude) => magnitude switch
    {
        2 => Cue.DOUBLE_KILL,
        3 => Cue.TRIPLE_KILL,
        _ => Cue.MULTI_KILL,
    };

    private static SoundEffect? Sound(Cue cue) => cue switch
    {
        Cue.FIRST_BLOOD => Sfx.Sounds.FirstBlood,
        Cue.HUMILIATION => Sfx.Sounds.Owned,
        Cue.SHUTDOWN => Sfx.Sounds.Shutdown,
        Cue.HOLY_SHIT => Sfx.Sounds.HolyShit,
        Cue.DOUBLE_KILL => Sfx.Sounds.DoubleKill,
        Cue.TRIPLE_KILL => Sfx.Sounds.TripleKill,
        Cue.MULTI_KILL => Sfx.Sounds.MultiKill,
        Cue.KILL_STREAK => Sfx.Sounds.KillStreak,
        _ => null,
    };
}
