using Godot;
using Serilog;
using Serilog.Events;

namespace Mortz.Shared.Logging;

/// <summary>Serilog bootstrap, built the first time something logs so an
/// autoload can log before the main scene is ready.</summary>
public static class MortzLog
{
    public const string LEVEL_FLAG = "--log-level";

    /// <summary>Bracketed tag every line carries, e.g. "net" or "server".</summary>
    private const string AREA = "Area";

    private const string CONSOLE_TEMPLATE =
        "{Timestamp:HH:mm:ss.fff} {Level:u3} [{Area}] {Message:lj}{NewLine}{Exception}";

    private const string FILE_TEMPLATE =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} [{Area}] {Message:lj}{NewLine}{Exception}";

    private const int RETAINED_DAYS = 14;

    static MortzLog() => Log.Logger = Build();

    /// <summary>A logger tagged with the area shown in brackets on every line.</summary>
    public static ILogger For(string area) => Log.Logger.ForContext(AREA, area);

    /// <summary>Flushes the file sink on the way out.</summary>
    public static void Flush() => Log.CloseAndFlush();

    private static Serilog.Core.Logger Build()
    {
        bool server = RunMode.IsDedicatedServer;
        LoggerConfiguration config = new LoggerConfiguration()
            .MinimumLevel.Is(ResolveLevel())
            .Enrich.WithProperty(AREA, server ? "server" : "client");

        // editor and terminal share one OS console, so only one runs at a time
        if (OS.HasFeature("editor"))
            config.WriteTo.Sink(new GodotSink(CONSOLE_TEMPLATE));
        else
            config.WriteTo.Console(outputTemplate: CONSOLE_TEMPLATE);

        AddFileSink(config, server ? "server" : "client");
        return config.CreateLogger();
    }

    private static void AddFileSink(LoggerConfiguration config, string role)
    {
        try
        {
            string directory = Path.Combine(MortzUserData.Resolve(), "logs");
            Directory.CreateDirectory(directory);
            // shared: a client that hosts runs a second process of the other
            // role, and nothing stops two of the same role on one machine.
            config.WriteTo.File(
                Path.Combine(directory, $"{role}-.log"),
                outputTemplate: FILE_TEMPLATE,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RETAINED_DAYS,
                shared: true);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"no log file, console only: {exception.Message}");
        }
    }

    private static LogEventLevel ResolveLevel()
    {
        string? requested = CmdArgs.GetValue(LEVEL_FLAG) ??
                            System.Environment.GetEnvironmentVariable("MORTZ_LOG_LEVEL");
        return requested?.ToLowerInvariant() switch
        {
            "verbose" or "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "info" or "information" => LogEventLevel.Information,
            "warn" or "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };
    }
}
