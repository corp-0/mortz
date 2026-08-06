using System.Text.Json.Serialization;
using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public sealed record BotInputPlan
{
    public const int MAX_FRAMES = 4096;
    public const int MAX_TICKS = 60 * 60 * 10;

    private readonly BotInputFrame[] _frames;

    public Guid Id { get; }
    public IReadOnlyList<BotInputFrame> Frames => _frames;

    [JsonConstructor]
    public BotInputPlan(Guid id, IReadOnlyList<BotInputFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (id == Guid.Empty)
            throw new ArgumentException("An input plan needs an id.", nameof(id));
        if (frames.Count == 0)
            throw new ArgumentException("An input plan needs at least one frame.", nameof(frames));
        if (frames.Count > MAX_FRAMES)
            throw new ArgumentException($"An input plan cannot exceed {MAX_FRAMES} frames.", nameof(frames));

        int ticks = 0;
        foreach (BotInputFrame frame in frames)
        {
            ticks = checked(ticks + frame.Ticks);
        }
        if (ticks > MAX_TICKS)
            throw new ArgumentException($"An input plan cannot exceed {MAX_TICKS} ticks.", nameof(frames));

        Id = id;
        _frames = frames.ToArray();
    }

    public static BotInputPlan Sequence(params BotInputFrame[] frames) =>
        new(Guid.NewGuid(), frames);

    public static BotInputPlan Press(InputButtons buttons, byte aim = 0) =>
        Sequence(
            new BotInputFrame(buttons, aim, 1),
            new BotInputFrame(InputButtons.NONE, aim, 1));

    public static BotInputPlan Hold(InputButtons buttons, ushort ticks, byte aim = 0) =>
        Sequence(new BotInputFrame(buttons, aim, ticks));

    public static BotInputPlan Wait(ushort ticks, byte aim = 0) =>
        Hold(InputButtons.NONE, ticks, aim);
}
