using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Announcements;

/// <summary>Collects the game events of one render frame into a
/// priority-ordered batch; the voice and the banner do their own suppression
/// and pacing on top. Also holds the match-point state for late
/// subscribers.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementDirector : Node
{
    private readonly List<GameEventMsg> _pending = new();

    public event Action<IReadOnlyList<GameEventMsg>>? BatchReady;
    public event Action<MatchPointMsg>? MatchPointChanged;

    public MatchPointMsg? MatchPoint { get; private set; }

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
        GameEventMsg[] batch = Order(_pending);
        _pending.Clear();
        BatchReady?.Invoke(batch);
    }

    private void OnGameEvent(GameEventMsg msg) => _pending.Add(msg);

    private void OnMatchPoint(MatchPointMsg msg)
    {
        MatchPoint = msg.Active ? msg : null;
        MatchPointChanged?.Invoke(msg);
    }

    /// <summary>Ties keep arrival order, so a kind's magnitudes stay in the
    /// order the server sent.</summary>
    internal static GameEventMsg[] Order(IEnumerable<GameEventMsg> events) =>
        events.OrderBy(e => Priority(e.Kind)).ToArray();

    private static int Priority(GameEventKind kind) => kind switch
    {
        GameEventKind.HOLY_SHIT => 0,
        GameEventKind.FIRST_BLOOD => 1,
        GameEventKind.SHUTDOWN => 2,
        GameEventKind.MULTI_KILL => 3,
        GameEventKind.KILL_STREAK => 4,
        _ => 5, // HUMILIATION: whoever plays it at all plays it last
    };
}
