using System.Text.Json.Serialization;
using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public readonly record struct BotInputFrame
{
    /// <summary>The ten bits InputSampler can produce; anything else is a typo.</summary>
    private const InputButtons SAMPLED =
        InputButtons.LEFT |
        InputButtons.RIGHT |
        InputButtons.JUMP |
        InputButtons.DASH |
        InputButtons.ROPE |
        InputButtons.UP |
        InputButtons.DOWN |
        InputButtons.FIRE |
        InputButtons.RELOAD |
        InputButtons.PARRY;

    public InputButtons Buttons { get; }
    public byte Aim { get; }
    public ushort Ticks { get; }

    [JsonConstructor]
    public BotInputFrame(InputButtons buttons, byte aim, ushort ticks)
    {
        if ((buttons & ~SAMPLED) != 0)
            throw new ArgumentOutOfRangeException(nameof(buttons), "Unknown bot input button.");
        if (ticks == 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), "An input frame needs at least one tick.");
        Buttons = buttons;
        Aim = aim;
        Ticks = ticks;
    }
}
