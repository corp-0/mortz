using System.Text;

namespace Mortz.Core.Text;

/// <summary>A Godot BBCode value assembled from escaped text and code-owned styles.</summary>
public sealed class RichText
{
    private readonly StringBuilder _builder = new();

    public RichText()
    {
    }

    public RichText(string? text) => Add(text);

    public static implicit operator string(RichText richText) => richText.ToString();

    public override string ToString() => _builder.ToString();

    /// <summary>Appends text; BBCode delimiters are escaped.</summary>
    public RichText Add(string? text)
    {
        _builder.Append(Escape(text));
        return this;
    }

    public RichText Add(string? text, Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _builder.Append(style.Apply(Escape(text)));
        return this;
    }

    /// <summary>Appends an already-safe rich-text value.</summary>
    public RichText Add(RichText richText)
    {
        ArgumentNullException.ThrowIfNull(richText);
        _builder.Append(richText._builder);
        return this;
    }

    public RichText Add(RichText fragment, Style style)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(style);
        _builder.Append(style.Apply(fragment.ToString()));
        return this;
    }

    /// <summary>Wraps everything added so far in the style.</summary>
    public RichText Wrap(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        string wrapped = style.Apply(_builder.ToString());
        _builder.Clear();
        _builder.Append(wrapped);
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
