using Godot;
using Mortz.Core.Chat;
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>One rolled-number chat line. A live roll spins through random
/// candidates at a decelerating tick before settling on the real value;
/// rebuilt history renders already settled.</summary>
public partial class RollLineLabel : RichTextLabel
{
    private const float SPIN_SECONDS = 1.3f;
    private const float FIRST_TICK_SECONDS = 0.04f;
    private const float TICK_SLOWDOWN = 1.22f;

    private string _senderName = "";
    private int _value;
    private float _remaining;
    private float _interval = FIRST_TICK_SECONDS;
    private float _tickIn;

    public static RollLineLabel Create(string senderName, int value, bool animate)
    {
        RollLineLabel label = new()
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = true,
            FitContent = true,
            _senderName = senderName,
            _value = value,
            _remaining = animate ? SPIN_SECONDS : 0f,
        };
        label.Text = animate
            ? label.Spinning(Random.Shared.Next(DiceRoll.MIN, DiceRoll.MAX + 1))
            : label.Settled();
        label.SetProcess(animate);
        return label;
    }

    public override void _Process(double delta)
    {
        _remaining -= (float)delta;
        if (_remaining <= 0f)
        {
            Text = Settled();
            SetProcess(false);
            return;
        }
        _tickIn -= (float)delta;
        if (_tickIn > 0f)
            return;
        _interval *= TICK_SLOWDOWN;
        _tickIn = _interval;
        Text = Spinning(Random.Shared.Next(DiceRoll.MIN, DiceRoll.MAX + 1));
    }

    private string Spinning(int candidate) => new RichText()
        .Bold().ApplyTo(_senderName)
        .Add(" rolls... ")
        .Add(candidate.ToString());

    private string Settled() => new RichText()
        .Bold().ApplyTo(_senderName)
        .Add(" rolled ")
        .Bold().ApplyTo(_value.ToString())
        .Add($" ({DiceRoll.MIN}-{DiceRoll.MAX})");
}
