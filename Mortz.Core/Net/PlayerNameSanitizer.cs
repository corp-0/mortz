namespace Mortz.Core.Net;

public static class PlayerNameSanitizer
{
    public static string Sanitize(string? value) =>
        SafeName.Sanitize(value, NetConfig.MAX_NAME_LENGTH);
}
