using Mortz.Client.Announcements;
using Xunit;

namespace Mortz.Tests.Client.Announcements;

public class AnnouncementQueueTests
{
    [Fact]
    public void LinesAreSpacedByTheGap()
    {
        AnnouncementQueue queue = new();
        queue.Push(GameAnnouncer.Cue.HOLY_SHIT, now: 0);
        queue.Push(GameAnnouncer.Cue.TRIPLE_KILL, now: 0);

        Assert.Equal(GameAnnouncer.Cue.HOLY_SHIT, queue.Next(0));
        Assert.Null(queue.Next(AnnouncementQueue.LINE_GAP_SECONDS / 2));
        Assert.Equal(GameAnnouncer.Cue.TRIPLE_KILL,
            queue.Next(AnnouncementQueue.LINE_GAP_SECONDS + 0.01));
    }

    [Fact]
    public void StaleLinesAreDroppedNotPlayedLate()
    {
        AnnouncementQueue queue = new();
        queue.Push(GameAnnouncer.Cue.DOUBLE_KILL, now: 0);

        Assert.Null(queue.Next(AnnouncementQueue.TTL_SECONDS + 1));
    }

    [Fact]
    public void ADroppedStaleLineDoesNotBlockAFreshOne()
    {
        AnnouncementQueue queue = new();
        queue.Push(GameAnnouncer.Cue.DOUBLE_KILL, now: 0);
        queue.Push(GameAnnouncer.Cue.BLOODLUST, now: AnnouncementQueue.TTL_SECONDS + 1);

        Assert.Equal(GameAnnouncer.Cue.BLOODLUST,
            queue.Next(AnnouncementQueue.TTL_SECONDS + 1));
    }

    [Fact]
    public void EmptyQueueStaysQuiet()
    {
        Assert.Null(new AnnouncementQueue().Next(10));
    }
}
