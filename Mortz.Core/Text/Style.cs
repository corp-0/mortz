namespace Mortz.Core.Text;

/// <summary>A reusable stack of rich-text styles, applied in add order so the
/// first added ends up innermost.</summary>
public sealed class Style
{
    private readonly List<IRichTextStyle> _styles = [];

    public Style Bold() => With(new BoldStyle());
    public Style Italic() => With(new ItalicStyle());
    public Style Underline() => With(new UnderlineStyle());
    public Style Strikethrough() => With(new StrikethroughStyle());
    public Style Code() => With(new CodeStyle());
    public Style Color(RichTextColor color) => With(new ColorStyle(color));
    public Style Color(string hexColor) => With(new ColorStyle(hexColor));
    public Style FontSize(int size) => With(new FontSizeStyle(size));
    public Style Center() => With(new CenterStyle());
    public Style Wave(float amplitude, float frequency) => With(new WaveStyle(amplitude, frequency));
    public Style Shake(float rate, int level) => With(new ShakeStyle(rate, level));
    public Style Tornado(float radius, float frequency) => With(new TornadoStyle(radius, frequency));
    public Style Pulse(float frequency, string hexColor, float ease) =>
        With(new PulseStyle(frequency, hexColor, ease));

    /// <summary>Pulses toward a palette color; alpha 0-1 sets the glow strength.</summary>
    public Style Pulse(float frequency, RichTextColor color, float alpha, float ease) =>
        With(new PulseStyle(frequency, color, alpha, ease));

    public Style With(IRichTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _styles.Add(style);
        return this;
    }

    public string Apply(string bbcode)
    {
        string styled = bbcode;
        foreach (IRichTextStyle style in _styles)
        {
            styled = style.Apply(styled);
        }
        return styled;
    }
}
