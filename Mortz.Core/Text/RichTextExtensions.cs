namespace Mortz.Core.Text;

/// <summary>Convenience styles for small, single-span values.</summary>
public static class RichTextExtensions
{
    public static string Bold(this string text) =>
        new RichText().Add(text, new Style().Bold()).ToString();

    public static string Italic(this string text) =>
        new RichText().Add(text, new Style().Italic()).ToString();

    public static string Underline(this string text) =>
        new RichText().Add(text, new Style().Underline()).ToString();

    public static string Strikethrough(this string text) =>
        new RichText().Add(text, new Style().Strikethrough()).ToString();

    public static string Code(this string text) =>
        new RichText().Add(text, new Style().Code()).ToString();

    public static string Color(this string text, RichTextColor color) =>
        new RichText().Add(text, new Style().Color(color)).ToString();

    public static string Color(this string text, string hexColor) =>
        new RichText().Add(text, new Style().Color(hexColor)).ToString();
}
