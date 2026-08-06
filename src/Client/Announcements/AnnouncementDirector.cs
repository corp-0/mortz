using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Match;
using Mortz.Core.Match.Events;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;

namespace Mortz.Client.Announcements;

/// <summary>Turns one render frame's game events into announcements. Keeps the
/// match-point state for late subscribers.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementDirector : Node, IAnnouncementDirector,
    IHandle<GameEventMsg>,
    IHandle<MatchPointMsg>
{
    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private readonly List<GameEventMsg> _pending = new();

    private NetRouter? _routed;

    public event Action<IReadOnlyList<Announcement>>? BatchReady;
    public event Action<MatchPointState?>? MatchPointChanged;

    public MatchPointState? MatchPoint { get; private set; }

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public override void _Process(double delta)
    {
        if (_pending.Count == 0)
            return;
        Announcement[] batch = Describe(_pending, Players.NameOf, Players.TeamOf);
        _pending.Clear();
        BatchReady?.Invoke(batch);
    }

    public void Handle(in GameEventMsg msg) => _pending.Add(msg);

    public void Handle(in MatchPointMsg msg)
    {
        MatchPoint? state = MatchProtocol.Decode(msg);
        MatchPoint = state is MatchPoint held ? Describe(held, Players.NameOf, Players.TeamOf) : null;
        MatchPointChanged?.Invoke(MatchPoint);
    }

    /// <summary>Folded, priority ordered (ties keep arrival order), names
    /// resolved.</summary>
    public static Announcement[] Describe(
        IReadOnlyList<GameEventMsg> events, Func<int, string> name, Func<int, Team?> team) =>
        DropCoveredRegularKills(FoldHolyShit(events))
            .OrderBy(e => Priority(e.Kind))
            .Select(e => new Announcement(
                e.Kind,
                Who(e.ActorId, name, team),
                Who(e.VictimId, name, team),
                e.Magnitude,
                (SuicideCause)e.Detail))
            .ToArray();

    private static IReadOnlyList<GameEventMsg> DropCoveredRegularKills(
        IReadOnlyList<GameEventMsg> batch)
    {
        HashSet<int> actorsWithSpecials = batch
            .Where(e => ReplacesRegularKill(e.Kind))
            .Select(e => e.ActorId)
            .ToHashSet();
        if (actorsWithSpecials.Count == 0)
            return batch;
        return batch
            .Where(e => e.Kind != GameEventKind.REGULAR_KILL ||
                        !actorsWithSpecials.Contains(e.ActorId))
            .ToArray();
    }

    public static MatchPointState Describe(
        MatchPoint held, Func<int, string> name, Func<int, Team?> team)
    {
        MatchPointLeader? leader = held.Leader switch
        {
            Victor.Team victor => new MatchPointLeader(Teams.Name(victor.Value), victor.Value),
            Victor.Player victor => new MatchPointLeader(name(victor.PeerId), team(victor.PeerId)),
            _ => null,
        };
        return new MatchPointState(held.Remaining, leader);
    }

    private static Combatant Who(int id, Func<int, string> name, Func<int, Team?> team) =>
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
        GameEventKind.HUMILIATION => 8,
        GameEventKind.REGULAR_KILL or GameEventKind.TEAM_KILL => 9,
        _ => 10,
    };

    private static bool ReplacesRegularKill(GameEventKind kind) => kind is
        GameEventKind.FIRST_BLOOD or
        GameEventKind.HUMILIATION or
        GameEventKind.SHUTDOWN or
        GameEventKind.HOLY_SHIT or
        GameEventKind.MULTI_KILL or
        GameEventKind.KILL_STREAK or
        GameEventKind.REVENGE or
        GameEventKind.TEAM_WIPE;
}
