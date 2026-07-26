using System.Globalization;

namespace Mortz.Core.Text;

public interface IRichTextStyle
{
    string Apply(string escapedText);
}

public enum RichTextColor
{
    BLACK,
    BLUE,
    GREEN,
    ORANGE,
    PURPLE,
    RED,
    WHITE,
    YELLOW,

    // Announcement palette.
    BLOOD_RED,
    GOLD,
    AMBER,
    EMBER,
    VERMILION,
    SCARLET,
    WHITE_HOT,
    MOSS,
    VENOM,
    ACID,
    ELECTRIC_LIME,
    ICE,
    ORCHID,
    FLAME,
}

public sealed class BoldStyle : IRichTextStyle
{
    public string Apply(string escapedText) => $"[b]{escapedText}[/b]";
}

public sealed class ItalicStyle : IRichTextStyle
{
    public string Apply(string escapedText) => $"[i]{escapedText}[/i]";
}

public sealed class UnderlineStyle : IRichTextStyle
{
    public string Apply(string escapedText) => $"[u]{escapedText}[/u]";
}

public sealed class StrikethroughStyle : IRichTextStyle
{
    public string Apply(string escapedText) => $"[s]{escapedText}[/s]";
}

public sealed class CodeStyle : IRichTextStyle
{
    public string Apply(string escapedText) => $"[code]{escapedText}[/code]";
}

public sealed class ColorStyle : IRichTextStyle
{
    private readonly string _color;

    public ColorStyle(RichTextColor color) => _color = Hex(color);

    internal static string Hex(RichTextColor color) => color switch
    {
        RichTextColor.BLACK => "#000000",
        RichTextColor.BLUE => "#0000ff",
        RichTextColor.GREEN => "#008000",
        RichTextColor.ORANGE => "#ffa500",
        RichTextColor.PURPLE => "#800080",
        RichTextColor.RED => "#ff0000",
        RichTextColor.WHITE => "#ffffff",
        RichTextColor.YELLOW => "#ffff00",
        RichTextColor.BLOOD_RED => "#c41414",
        RichTextColor.GOLD => "#ffd94d",
        RichTextColor.AMBER => "#ffb52e",
        RichTextColor.EMBER => "#ff8c1a",
        RichTextColor.VERMILION => "#ff5c33",
        RichTextColor.SCARLET => "#ff2e2e",
        RichTextColor.WHITE_HOT => "#fff0e0",
        RichTextColor.MOSS => "#a8cf5a",
        RichTextColor.VENOM => "#7de83a",
        RichTextColor.ACID => "#4dff21",
        RichTextColor.ELECTRIC_LIME => "#baff8a",
        RichTextColor.ICE => "#99d9ff",
        RichTextColor.ORCHID => "#ff8cd9",
        RichTextColor.FLAME => "#ff7333",
        _ => throw new ArgumentOutOfRangeException(nameof(color)),
    };

    public ColorStyle(string hexColor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hexColor);
        if (!IsHexColor(hexColor))
            throw new ArgumentException("Colors must use #RGB, #RGBA, #RRGGBB, or #RRGGBBAA.",
                nameof(hexColor));
        _color = hexColor;
    }

    public string Apply(string escapedText) => $"[color={_color}]{escapedText}[/color]";

    internal static bool IsHexColor(string value)
    {
        if (value[0] != '#' || value.Length is not (4 or 5 or 7 or 9))
            return false;
        return value.AsSpan(1).ToString().All(char.IsAsciiHexDigit);
    }
}

public sealed class FontSizeStyle : IRichTextStyle
{
    private readonly int _size;

    public FontSizeStyle(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        _size = size;
    }

    public string Apply(string escapedText) => $"[font_size={_size}]{escapedText}[/font_size]";
}

public sealed class CenterStyle : IRichTextStyle
{
    public string Apply(string escapedText) => $"[center]{escapedText}[/center]";
}

public sealed class WaveStyle : IRichTextStyle
{
    private readonly float _amplitude;
    private readonly float _frequency;

    public WaveStyle(float amplitude, float frequency)
    {
        _amplitude = amplitude;
        _frequency = frequency;
    }

    public string Apply(string escapedText) =>
        $"[wave amp={BbNumber.Bb(_amplitude)} freq={BbNumber.Bb(_frequency)}]{escapedText}[/wave]";
}

public sealed class ShakeStyle : IRichTextStyle
{
    private readonly float _rate;
    private readonly int _level;

    public ShakeStyle(float rate, int level)
    {
        _rate = rate;
        _level = level;
    }

    public string Apply(string escapedText) =>
        $"[shake rate={BbNumber.Bb(_rate)} level={_level}]{escapedText}[/shake]";
}

public sealed class TornadoStyle : IRichTextStyle
{
    private readonly float _radius;
    private readonly float _frequency;

    public TornadoStyle(float radius, float frequency)
    {
        _radius = radius;
        _frequency = frequency;
    }

    public string Apply(string escapedText) =>
        $"[tornado radius={BbNumber.Bb(_radius)} freq={BbNumber.Bb(_frequency)}]{escapedText}[/tornado]";
}

public sealed class PulseStyle : IRichTextStyle
{
    private readonly float _frequency;
    private readonly string _color;
    private readonly float _ease;

    public PulseStyle(float frequency, RichTextColor color, float alpha, float ease)
        : this(frequency, WithAlpha(color, alpha), ease)
    {
    }

    private static string WithAlpha(RichTextColor color, float alpha)
    {
        byte channel = (byte)Math.Round(Math.Clamp(alpha, 0f, 1f) * 255f);
        return ColorStyle.Hex(color) + channel.ToString("x2");
    }

    public PulseStyle(float frequency, string hexColor, float ease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hexColor);
        if (!ColorStyle.IsHexColor(hexColor))
            throw new ArgumentException("Colors must use #RGB, #RGBA, #RRGGBB, or #RRGGBBAA.",
                nameof(hexColor));
        _frequency = frequency;
        _color = hexColor;
        _ease = ease;
    }

    public string Apply(string escapedText) =>
        $"[pulse freq={BbNumber.Bb(_frequency)} color={_color} ease={BbNumber.Bb(_ease)}]{escapedText}[/pulse]";
}

file static class BbNumber
{
    // Godot parses BBCode numbers with a dot regardless of locale.
    public static string Bb(float value) => value.ToString("0.0###", CultureInfo.InvariantCulture);
}
