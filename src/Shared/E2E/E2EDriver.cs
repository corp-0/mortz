#if TOOLS
using System.Collections.Concurrent;
using System.Text.Json;
using Godot;
using Mortz.E2E.Protocol;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Shared.E2E;

/// <summary>The only stdin reader in the game, inert unless E2ELaunch.Enabled. A
/// background thread queues lines; _Process drains them on the main thread so
/// no Godot object is touched off-thread.</summary>
public partial class E2EDriver : Node
{
    private static readonly ILogger _log = MortzLog.For("e2e");

    private const int MIN_PHYSICS_STEPS = 8;

    private readonly ConcurrentQueue<string> _lines = new();
    private readonly E2EStdoutWriter _writer = new();

    private IE2EHandler? _handler;
    private volatile bool _armed;
    private volatile bool _eof;
    private bool _quitting;

    /// <summary>Structured stdout. Handlers use this to emit events outside
    /// request handling - a phase change has no correlation id to answer.</summary>
    public IE2EResponder Responder => _writer;

    public override void _Ready()
    {
        if (!E2ELaunch.Enabled)
        {
            SetProcess(false);
            return;
        }

        Engine.TimeScale = E2ELaunch.Timescale;
        Engine.MaxPhysicsStepsPerFrame =
            Math.Max(MIN_PHYSICS_STEPS, (int)(MIN_PHYSICS_STEPS * E2ELaunch.Timescale));
        Thread reader = new(ReadStdin) { IsBackground = true, Name = "e2e-stdin" };
        reader.Start();
    }

    /// <summary>Called by the role handler once it can serve requests.</summary>
    public void Attach(IE2EHandler handler)
    {
        if (!E2ELaunch.Enabled)
            return;
        _handler = handler;
        _writer.Emit(new ProcessReadyEvent(
            handler.Role, (int)OS.GetProcessId(), E2EProtocolSchema.Hash));
    }

    public override void _Process(double delta)
    {
        while (_lines.TryDequeue(out string? line))
        {
            Dispatch(line);
        }
        if (_eof && !_quitting)
            Quit(E2EExitCode.STDIN_EOF);
    }

    private void ReadStdin()
    {
        while (Console.In.ReadLine() is string line)
        {
            if (line.Length == 0)
                continue;
            _armed = true;
            _lines.Enqueue(line);
        }

        if (_armed)
        {
            _eof = true;
            return;
        }
        // --e2e under systemd, nohup or docker without a TTY gets instant EOF.
        // Exiting 0 there would look like a clean shutdown, so stay up instead and complain loudly.
        _log.Error("stdin closed before any request; --e2e requires a driving harness; " +
                   "not treating EOF as shutdown");
    }

    private void Dispatch(string line)
    {
        E2ERequest request;
        try
        {
            request = E2EWire.DeserializeRequest(line);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            Reject(line, exception);
            return;
        }

        switch (request)
        {
            case PingRequest ping:
                _writer.Respond(new PongResponse(ping.Id, E2EProtocolSchema.Hash));
                return;
            case ShutdownRequest shutdown:
                _writer.Respond(new ShutdownStartedResponse(shutdown.Id));
                Quit(E2EExitCode.CLEAN);
                return;
        }

        if (_handler is not IE2EHandler handler)
        {
            Fail(request.Id, E2EError.INVALID_STATE, "No E2E handler is attached yet.");
            return;
        }
        if (!AcceptedBy(handler.Role, request))
        {
            Fail(request.Id, E2EError.UNKNOWN_COMMAND,
                $"{request.GetType().Name} is not served by the {handler.Role} process.");
            return;
        }

        handler.Handle(request, _writer);
    }

    private static bool AcceptedBy(E2EProcessRole role, E2ERequest request) => role switch
    {
        E2EProcessRole.SERVER => request is IServerRequest,
        E2EProcessRole.CLIENT => request is IClientRequest,
        _ => false,
    };

    /// <summary>A malformed line gets a typed answer if its id survives.
    /// Otherwise there's no correlation id to answer, just a log line.</summary>
    private void Reject(string line, Exception exception)
    {
        if (RecoverId(line) is Guid id)
        {
            Fail(id, E2EError.INVALID_REQUEST, exception.Message);
            return;
        }
        _log.Error(exception, "unparseable request line dropped");
    }

    private static Guid? RecoverId(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!document.RootElement.TryGetProperty("id", out JsonElement id))
                return null;
            return id.TryGetGuid(out Guid value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Fail(Guid id, E2EError error, string message) =>
        _writer.Respond(new CommandFailedResponse(id, error, message));

    private void Quit(int exitCode)
    {
        _quitting = true;
        _writer.Flush();
        // Finalize managed wrappers of long-freed scenes while the runtime is
        // still healthy: letting them race engine teardown intermittently hits
        // Godot's "!rc_owner" FATAL in the mono module and exits 134.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(exitCode);
    }
}
#endif
