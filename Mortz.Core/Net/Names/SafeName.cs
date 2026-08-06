using System.Globalization;
using System.Text;

namespace Mortz.Core.Net.Names;

/// <summary>Drops control and invisible format runes, then caps UTF-16 length.</summary>
public static class SafeName
{
    public static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        StringBuilder result = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (Rune rune in value.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                continue;
            if (result.Length + rune.Utf16SequenceLength > maxLength)
                break;
            result.Append(rune);
        }
        return result.ToString().Trim();
    }
}
