using System.Text;

namespace Mortz.Core.Text;

/// <summary>A Godot BBCode value assembled from escaped text and code-owned styles.</summary>
public sealed class RichText
{
    private readonly StringBuilder _builder = new();
    private readonly List<IRichTextStyle> _styles = [];

    public RichText()
    {
    }

    /// <summary>Creates rich text containing unstyled, escaped text.</summary>
    public RichText(string? text) => Add(text);

    public static implicit operator string(RichText richText) => richText.ToString();

    public override string ToString() => _builder.ToString();

    public RichText Bold() => Style(new BoldStyle());
    public RichText Italic() => Style(new ItalicStyle());
    public RichText Underline() => Style(new UnderlineStyle());
    public RichText Strikethrough() => Style(new StrikethroughStyle());
    public RichText Code() => Style(new CodeStyle());
    public RichText Color(RichTextColor color) => Style(new ColorStyle(color));
    public RichText Color(string hexColor) => Style(new ColorStyle(hexColor));
    public RichText FontSize(int size) => Style(new FontSizeStyle(size));

    /// <summary>Adds a style to be applied by the next <see cref="ApplyTo"/> call.</summary>
    public RichText Style(IRichTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _styles.Add(style);
        return this;
    }

    /// <summary>
    /// Escapes <paramref name="text"/>, applies the accumulated styles, appends it,
    /// and clears the style stack.
    /// </summary>
    public RichText ApplyTo(string? text)
    {
        string styled = Escape(text);
        foreach (IRichTextStyle style in _styles)
        {
            styled = style.Apply(styled);
        }
        _builder.Append(styled);
        _styles.Clear();
        return this;
    }

    /// <summary>Appends unstyled text. BBCode delimiters are always escaped.</summary>
    public RichText Add(string? text)
    {
        _builder.Append(Escape(text));
        return this;
    }

    /// <summary>Appends an already-safe rich-text value.</summary>
    public RichText Add(RichText richText)
    {
        ArgumentNullException.ThrowIfNull(richText);
        _builder.Append(richText._builder);
        return this;
    }

    public RichText AddOnNewLine(string? text) => Add("\n").Add(text);

    internal static RichText FromTrustedBbCode(string bbcode) => new RichText().AppendRaw(bbcode);

    private RichText AppendRaw(string bbcode)
    {
        _builder.Append(bbcode);
        return this;
    }

    internal static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var escaped = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            switch (character)
            {
                case '[':
                    escaped.Append("[lb]");
                    break;
                case ']':
                    escaped.Append("[rb]");
                    break;
                default:
                    escaped.Append(character);
                    break;
            }
        }
        return escaped.ToString();
    }
}
