using Mortz.Core.Sim;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Named deadlines, all scaled by MORTZ_E2E_TIMEOUT_SCALE so a slow machine
/// widens every wait at once. Timescale is treated as an upper bound on the
/// speedup, never as a promise: under CPU pressure Godot clamps the physics
/// steps and game time falls behind wall time.
/// </summary>
public sealed class E2EBudget
{
    /// <summary>Half again on top of the ideal, so a partly-degraded speedup is
    /// slow rather than red.</summary>
    private const double SIM_SLACK = 1.5;

    private static readonly TimeSpan _simFloor = TimeSpan.FromSeconds(2);

    private readonly double _timescale;
    private readonly double _scale;

    public E2EBudget(double timescale) : this(timescale, EnvironmentScale)
    {
    }

    public E2EBudget(double timescale, double scale)
    {
        if (!double.IsFinite(timescale) || timescale <= 0)
            throw new ArgumentOutOfRangeException(nameof(timescale));
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        _timescale = timescale;
        _scale = scale;
    }

    /// <summary>Process boot through the ready event; a cold Godot start is slow.</summary>
    public TimeSpan Ready => Scaled(TimeSpan.FromSeconds(60));

    public TimeSpan Connect => Scaled(TimeSpan.FromSeconds(30));

    public TimeSpan Response => Scaled(TimeSpan.FromSeconds(15));

    public TimeSpan MatchEvent => Scaled(TimeSpan.FromSeconds(60));

    public TimeSpan Shutdown => Scaled(TimeSpan.FromSeconds(20));

    /// <summary>Wall time a run of sim ticks should need, with slack.</summary>
    public TimeSpan SimTicks(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        double seconds = ticks / (SimConfig.TICK_RATE * _timescale) * SIM_SLACK;
        TimeSpan ideal = TimeSpan.FromSeconds(seconds);
        return Scaled(ideal < _simFloor ? _simFloor : ideal);
    }

    private TimeSpan Scaled(TimeSpan value) => value * _scale;

    /// <summary>MORTZ_E2E_TIMEOUT_SCALE, 1 when unset. Public so a caller that
    /// needs extra slack multiplies it rather than replacing it.</summary>
    public static double EnvironmentScale => ReadEnvironmentScale();

    private static double ReadEnvironmentScale()
    {
        string? configured = Environment.GetEnvironmentVariable("MORTZ_E2E_TIMEOUT_SCALE");
        if (configured == null || !double.TryParse(configured,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double scale) ||
            !double.IsFinite(scale) || scale <= 0)
            return 1;
        return scale;
    }
}
