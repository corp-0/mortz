namespace Mortz.Client.Announcements;

public interface IAnnouncementDirector
{
    event Action<IReadOnlyList<Announcement>>? BatchReady;
    event Action<MatchPointState>? MatchPointChanged;

    MatchPointState? MatchPoint { get; }
}
