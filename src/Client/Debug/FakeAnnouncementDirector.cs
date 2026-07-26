using Mortz.Client.Announcements;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Debug;

public sealed class FakeAnnouncementDirector : IAnnouncementDirector
{
    public event Action<IReadOnlyList<Announcement>>? BatchReady;
    public event Action<MatchPointState>? MatchPointChanged;

    public MatchPointState? MatchPoint { get; private set; }

    public void Fire(params GameEventMsg[] events) =>
        BatchReady?.Invoke(AnnouncementDirector.Describe(events, DebugName, static _ => 0));

    public void SetMatchPoint(MatchPointMsg msg)
    {
        MatchPointState state = AnnouncementDirector.Describe(msg, DebugName);
        MatchPoint = state.Active ? state : null;
        MatchPointChanged?.Invoke(state);
    }

    private static string DebugName(long id) => $"Player {id}";
}
