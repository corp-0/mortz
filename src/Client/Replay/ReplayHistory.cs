using Mortz.Core.Sim;

namespace Mortz.Client.Replay;

/// <summary>A short rolling copy of exactly what was presented on screen. It
/// is detached from prediction and terrain, so sampling it cannot affect the
/// authoritative client state.</summary>
internal sealed class ReplayHistory
{
    internal const float HISTORY_TICKS = SimConfig.TICK_RATE;
    internal const float REPLAY_TICKS = SimConfig.TICK_RATE * 0.75f;

    private readonly List<ReplayFrame> _frames = new();

    public void Add(ReplayFrame frame)
    {
        _frames.Add(frame);
        float oldest = frame.Tick - HISTORY_TICKS;
        int remove = _frames.FindIndex(f => f.Tick >= oldest);
        if (remove > 0)
            _frames.RemoveRange(0, remove);
    }

    public ReplayClip? Capture(int eventTick)
    {
        if (_frames.Count < 2)
            return null;
        float end = Math.Min(eventTick, _frames[^1].Tick);
        int last = _frames.FindLastIndex(frame => frame.Tick <= end);
        if (last < 1)
            return null;
        float startTick = end - REPLAY_TICKS;
        int first = _frames.FindLastIndex(last, last + 1, frame => frame.Tick <= startTick);
        first = Math.Max(0, first);
        ReplayFrame[] frames = _frames.GetRange(first, last - first + 1).ToArray();
        if (frames[^1].Tick - frames[0].Tick < SimConfig.TICK_RATE * 0.25f)
            return null;
        return new ReplayClip(frames);
    }
}
