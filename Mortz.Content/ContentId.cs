using System.Text.RegularExpressions;

namespace Mortz.Content;

public static partial class ContentId
{
    public static bool IsValid(string value) => Pattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
