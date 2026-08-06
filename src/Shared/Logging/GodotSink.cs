using Godot;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Mortz.Shared.Logging;

/// <summary>Serilog sink that writes to the Godot console, for editor runs.</summary>
public sealed class GodotSink(string outputTemplate) : ILogEventSink
{
    private readonly MessageTemplateTextFormatter _formatter = new(outputTemplate, null);

    public void Emit(LogEvent logEvent)
    {
        StringWriter writer = new();
        _formatter.Format(logEvent, writer);
        string line = writer.ToString().TrimEnd();

        if (logEvent.Level >= LogEventLevel.Error)
            GD.PrintErr(line);
        else if (logEvent.Level == LogEventLevel.Warning)
            GD.PushWarning(line);
        else
            GD.Print(line);
    }
}
