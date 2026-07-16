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

    public ColorStyle(RichTextColor color) => _color = color.ToString().ToLowerInvariant();

    public ColorStyle(string hexColor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hexColor);
        if (!IsHexColor(hexColor))
            throw new ArgumentException("Colors must use #RGB, #RGBA, #RRGGBB, or #RRGGBBAA.",
                nameof(hexColor));
        _color = hexColor;
    }

    public string Apply(string escapedText) => $"[color={_color}]{escapedText}[/color]";

    private static bool IsHexColor(string value)
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
