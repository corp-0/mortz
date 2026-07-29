using Mortz.Client.Announcements;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Debug;

public sealed class FakeAnnouncementDirector : IAnnouncementDirector
{
    public event Action<IReadOnlyList<Announcement>>? BatchReady;
    public event Action<MatchPointState?>? MatchPointChanged;

    public MatchPointState? MatchPoint { get; private set; }

    public void Fire(params GameEventMsg[] events) =>
        BatchReady?.Invoke(AnnouncementDirector.Describe(events, DebugName, DebugTeam));

    public void SetMatchPoint(MatchPoint? state)
    {
        MatchPoint = state is MatchPoint held
            ? AnnouncementDirector.Describe(held, DebugName, DebugTeam)
            : null;
        MatchPointChanged?.Invoke(MatchPoint);
    }

    private static string DebugName(long id) => $"Player {id}";

    /// <summary>Nobody has a side in the debug scene.</summary>
    private static Team? DebugTeam(long id) => null;
}
