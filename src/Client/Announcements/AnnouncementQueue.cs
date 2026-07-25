namespace Mortz.Client.Announcements;

/// <summary>Paces voice lines: a minimum gap between starts so chained cues
/// don't talk over each other, and a TTL so a line that waited too long is
/// dropped instead of played out of context.</summary>
internal sealed class AnnouncementQueue
{
    public const double LINE_GAP_SECONDS = 1.1;
    public const double TTL_SECONDS = 4.0;

    private readonly Queue<(GameAnnouncer.Cue Cue, double QueuedAt)> _lines = new();
    private double _lastStart = double.NegativeInfinity;

    public void Push(GameAnnouncer.Cue cue, double now) => _lines.Enqueue((cue, now));

    /// <summary>The line to start now, or null while pacing or empty.</summary>
    public GameAnnouncer.Cue? Next(double now)
    {
        if (now - _lastStart < LINE_GAP_SECONDS)
            return null;
        while (_lines.Count > 0)
        {
            (GameAnnouncer.Cue cue, double queuedAt) = _lines.Dequeue();
            if (now - queuedAt > TTL_SECONDS)
                continue;
            _lastStart = now;
            return cue;
        }
        return null;
    }
}
