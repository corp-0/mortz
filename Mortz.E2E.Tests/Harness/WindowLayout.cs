namespace Mortz.E2E.Tests.Harness;

/// <summary>Tiles watched clients so two of them never sit on top of each
/// other. Small and side by side beats fullscreen when you want to see both.</summary>
public static class WindowLayout
{
    public const int WIDTH = 640;
    public const int HEIGHT = 360;

    private const int COLUMNS = 2;
    private const int MARGIN = 24;
    /// <summary>Room for the title bar, so a row below does not hide it.</summary>
    private const int TITLE_BAR = 40;

    public static WindowPlacement For(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int column = index % COLUMNS;
        int row = index / COLUMNS;
        return new WindowPlacement(
            MARGIN + column * (WIDTH + MARGIN),
            MARGIN + row * (HEIGHT + MARGIN + TITLE_BAR),
            WIDTH,
            HEIGHT);
    }
}
