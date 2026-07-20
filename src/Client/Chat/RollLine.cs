using Godot;
using Mortz.Core.Chat;
using Mortz.Core.Text;

namespace Mortz.Client.Chat;

/// <summary>One rolled-number chat line: a live roll scrolls a reel that eases
/// to a stop on the real value; rebuilt history renders already settled.</summary>
public partial class RollLine : HBoxContainer
{
    private const float SPIN_SECONDS = 2.2f;
    private const int REEL_ROWS = 32;

    private string _senderName = "";
    private int _value;
    private float _elapsed;
    private float _travel;
    private Control _column = null!;

    public static RollLine Create(string senderName, int value, bool animate)
    {
        // Never takes the mouse; the in-game overlay stays click-through.
        RollLine line = new()
        {
            _senderName = senderName,
            _value = value,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (animate)
            line.BuildReel();
        else
            line.AddChild(line.BuildSettledLabel());
        line.SetProcess(animate);
        return line;
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= SPIN_SECONDS)
        {
            Settle();
            return;
        }
        float left = 1f - _elapsed / SPIN_SECONDS;
        // cubic ease-out: gentle brake, so the slowdown stays watchable
        float eased = 1f - left * left * left;
        _column.Position = new Vector2(0f, -_travel * eased);
    }

    private void BuildReel()
    {
        Font font = GetThemeDefaultFont();
        int fontSize = GetThemeDefaultFontSize();
        float rowHeight = font.GetHeight(fontSize);
        float rowWidth = font.GetStringSize(DiceRoll.MAX.ToString(),
            fontSize: fontSize).X;
        _travel = (REEL_ROWS - 1) * rowHeight;

        AddChild(BuildPrefixLabel(font, fontSize));

        Control window = new()
        {
            ClipContents = true,
            CustomMinimumSize = new Vector2(rowWidth, rowHeight),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _column = new Control { MouseFilter = MouseFilterEnum.Ignore };
        for (int i = 0; i < REEL_ROWS; i++)
        {
            int shown = i == REEL_ROWS - 1
                ? _value
                : Random.Shared.Next(DiceRoll.MIN, DiceRoll.MAX + 1);
            _column.AddChild(new Label
            {
                Text = shown.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(0f, i * rowHeight),
                Size = new Vector2(rowWidth, rowHeight),
            });
        }
        window.AddChild(_column);
        AddChild(window);
    }

    // RichTextLabel won't size to its text, so measure the prefix ourselves;
    // the 2px slack covers faux-bold widening
    private RichTextLabel BuildPrefixLabel(Font font, int fontSize)
    {
        string prefix = $"{_senderName} rolls";
        return new RichTextLabel
        {
            Text = new RichText().Bold().ApplyTo(_senderName).Add(" rolls"),
            BbcodeEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(
                font.GetStringSize(prefix, fontSize: fontSize).X + 2f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private void Settle()
    {
        SetProcess(false);
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        AddChild(BuildSettledLabel());
    }

    private RichTextLabel BuildSettledLabel() => new()
    {
        Text = new RichText().Bold().ApplyTo(_senderName)
            .Add(" rolled ")
            .Bold().ApplyTo(_value.ToString())
            .Add($" ({DiceRoll.MIN}-{DiceRoll.MAX})"),
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        BbcodeEnabled = true,
        FitContent = true,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        MouseFilter = MouseFilterEnum.Ignore,
    };
}
