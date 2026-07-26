using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Roster;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Announcements;

/// <summary>Turns one render frame's game events into announcements. Keeps the
/// match-point state for late subscribers.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementDirector : Node, IAnnouncementDirector
{
    [Dependency]
    private ClientRoster Roster => this.DependOn<ClientRoster>();

    private readonly List<GameEventMsg> _pending = new();

    public event Action<IReadOnlyList<Announcement>>? BatchReady;
    public event Action<MatchPointState>? MatchPointChanged;

    public MatchPointState? MatchPoint { get; private set; }

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        GameEventMsg.Received += OnGameEvent;
        MatchPointMsg.Received += OnMatchPoint;
    }

    public void OnExitTree()
    {
        GameEventMsg.Received -= OnGameEvent;
        MatchPointMsg.Received -= OnMatchPoint;
    }

    public override void _Process(double delta)
    {
        if (_pending.Count == 0)
            return;
        Announcement[] batch = Describe(_pending, Roster.NameOf, Roster.TeamOf);
        _pending.Clear();
        BatchReady?.Invoke(batch);
    }

    private void OnGameEvent(GameEventMsg msg) => _pending.Add(msg);

    private void OnMatchPoint(MatchPointMsg msg)
    {
        MatchPointState state = Describe(msg, Roster.NameOf);
        MatchPoint = state.Active ? state : null;
        MatchPointChanged?.Invoke(state);
    }

    /// <summary>Folded, priority ordered (ties keep arrival order), names
    /// resolved.</summary>
    internal static Announcement[] Describe(
        IReadOnlyList<GameEventMsg> events, Func<long, string> name, Func<long, byte> team) =>
        FoldHolyShit(events)
            .OrderBy(e => Priority(e.Kind))
            .Select(e => new Announcement(
                e.Kind,
                Who(e.ActorId, name, team),
                Who(e.VictimId, name, team),
                e.Magnitude,
                (SuicideCause)e.Detail))
            .ToArray();

    internal static MatchPointState Describe(MatchPointMsg msg, Func<long, string> name)
    {
        if (msg.LeaderId == 0)
            return new(msg.Active, msg.Remaining, null);
        string leader = msg.LeaderIsTeam ? $"Team {msg.LeaderId}" : name(msg.LeaderId);
        return new(msg.Active, msg.Remaining, leader);
    }

    private static Combatant Who(long id, Func<long, string> name, Func<long, byte> team) =>
        new(id, name(id), team(id));

    /// <summary>Drops an actor's MULTI_KILL into their HOLY_SHIT, which then
    /// speaks the longer of the two magnitudes.</summary>
    private static IReadOnlyList<GameEventMsg> FoldHolyShit(IReadOnlyList<GameEventMsg> batch)
    {
        if (!batch.Any(e => e.Kind == GameEventKind.HOLY_SHIT))
            return batch;
        List<GameEventMsg> folded = new(batch.Count);
        foreach (GameEventMsg e in batch)
        {
            switch (e.Kind)
            {
                case GameEventKind.HOLY_SHIT:
                    byte chain = batch
                        .Where(other => other.Kind == GameEventKind.MULTI_KILL &&
                                        other.ActorId == e.ActorId)
                        .Select(other => other.Magnitude)
                        .DefaultIfEmpty(e.Magnitude)
                        .Max();
                    folded.Add(e with { Magnitude = Math.Max(chain, e.Magnitude) });
                    break;
                case GameEventKind.MULTI_KILL when batch.Any(other =>
                    other.Kind == GameEventKind.HOLY_SHIT && other.ActorId == e.ActorId):
                    break;
                default:
                    folded.Add(e);
                    break;
            }
        }
        return folded;
    }

    private static int Priority(GameEventKind kind) => kind switch
    {
        GameEventKind.HOLY_SHIT => 0,
        GameEventKind.FIRST_BLOOD => 1,
        GameEventKind.TEAM_WIPE => 2,
        GameEventKind.SHUTDOWN => 3,
        GameEventKind.MULTI_KILL => 4,
        GameEventKind.KILL_STREAK => 5,
        GameEventKind.REVENGE => 6,
        GameEventKind.SUICIDE => 7,
        _ => 8, // HUMILIATION: whoever plays it at all plays it last
    };
}
