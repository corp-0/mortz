#if TOOLS
using Godot;

namespace Mortz.Shared.E2E;

/// <summary>Single owner of the E2E flags. This file sits behind #if TOOLS, so
/// no export contains it; skip adding a runtime template_release check, the
/// compiler already rules that case out.</summary>
public static class E2ELaunch
{
    private const double MIN_TIMESCALE = 1;
    private const double MAX_TIMESCALE = 4;

    public static bool Enabled { get; } = CmdArgs.HasFlag("--e2e");

    /// <summary>1 unless an E2E process asked for compressed real time.</summary>
    public static double Timescale { get; } = ReadTimescale();

    /// <summary>Artificial delay before a lobby or match screen enters the tree.
    /// Exercises server readiness against genuinely absent screen handlers.</summary>
    public static int ScreenLoadDelayMs { get; } = Enabled
        ? Math.Clamp(CmdArgs.GetInt("--e2e-screen-load-delay-ms", 0), 0, 15_000)
        : 0;

    private static double ReadTimescale()
    {
        if (!Enabled)
            return 1;
        string? value = CmdArgs.GetValue("--timescale");
        if (value == null || !double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double timescale) ||
            !double.IsFinite(timescale))
        {
            return 1;
        }

        return Math.Clamp(timescale, MIN_TIMESCALE, MAX_TIMESCALE);
    }
}
#endif
