namespace Mortz.Core.Text;

/// <summary>Convenience styles for small, single-span values.</summary>
public static class RichTextExtensions
{
    public static string Bold(this string text) =>
        new RichText().Bold().ApplyTo(text).ToString();

    public static string Italic(this string text) =>
        new RichText().Italic().ApplyTo(text).ToString();

    public static string Underline(this string text) =>
        new RichText().Underline().ApplyTo(text).ToString();

    public static string Strikethrough(this string text) =>
        new RichText().Strikethrough().ApplyTo(text).ToString();

    public static string Code(this string text) =>
        new RichText().Code().ApplyTo(text).ToString();

    public static string Color(this string text, RichTextColor color) =>
        new RichText().Color(color).ApplyTo(text).ToString();

    public static string Color(this string text, string hexColor) =>
        new RichText().Color(hexColor).ApplyTo(text).ToString();
}
