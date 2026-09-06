namespace Mortz.Client.Announcements;

public interface IAnnouncementDirector
{
    event Action<IReadOnlyList<Announcement>>? BatchReady;

    /// <summary>Null when match point lapsed.</summary>
    event Action<MatchPointState?>? MatchPointChanged;

    MatchPointState? MatchPoint { get; }
}
