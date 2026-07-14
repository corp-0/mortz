using System.Globalization;
using System.Text;

namespace Mortz.Core;

public static class PlayerNameSanitizer
{
    /// <summary>Strips log/UI control and invisible format runes, then caps UTF-16 length.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var result = new StringBuilder(Math.Min(value.Length, NetConfig.MAX_NAME_LENGTH));
        foreach (Rune rune in value.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                continue;
            if (result.Length + rune.Utf16SequenceLength > NetConfig.MAX_NAME_LENGTH)
                break;
            result.Append(rune);
        }
        return result.ToString().Trim();
    }
}
