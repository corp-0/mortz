#if TOOLS
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;

namespace Mortz.Client.E2E;

/// <summary>
/// An input plan resolved against the predictor's sequence numbers. One
/// physics tick consumes exactly one sequence, so a plan installed at sequence
/// N owns N .. N + total ticks - 1. Pure math, no engine.
/// </summary>
public sealed class BotInputScript
{
    private readonly BotInputFrame[] _frames;

    public BotInputScript(BotInputPlan plan, int firstSequence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _frames = plan.Frames.ToArray();
        PlanId = plan.Id;
        FirstSequence = firstSequence;
        int ticks = 0;
        foreach (BotInputFrame frame in _frames)
        {
            ticks += frame.Ticks;
        }
        LastSequence = firstSequence + ticks - 1;
    }

    public Guid PlanId { get; }

    public int FirstSequence { get; }

    /// <summary>The last sequence this plan drives; inclusive.</summary>
    public int LastSequence { get; }

    public bool IsDone(int sequence) => sequence > LastSequence;

    public InputButtons ButtonsAt(int sequence) => FrameAt(sequence).Buttons;

    public byte AimAt(int sequence) => FrameAt(sequence).Aim;

    private BotInputFrame FrameAt(int sequence)
    {
        int offset = sequence - FirstSequence;
        if (offset < 0)
            return _frames[0];
        foreach (BotInputFrame frame in _frames)
        {
            if (offset < frame.Ticks)
                return frame;
            offset -= frame.Ticks;
        }
        return _frames[^1];
    }
}
#endif
