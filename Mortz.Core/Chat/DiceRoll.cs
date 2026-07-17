namespace Mortz.Core.Chat;

/// <summary>The public dice roll: the server picks the value, clients only
/// display values inside the advertised range.</summary>
public static class DiceRoll
{
    public const int MIN = 1;
    public const int MAX = 100;

    public static bool TryParse(string? text, out int value) =>
        int.TryParse(text, out value) && value is >= MIN and <= MAX;
}
