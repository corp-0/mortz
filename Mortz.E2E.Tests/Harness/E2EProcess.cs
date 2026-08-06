using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Mortz.E2E.Protocol;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Transport for one child process: start it, pump both streams, correlate
/// responses, shut it down. It knows nothing about roles; the drivers do.
/// </summary>
public sealed class E2EProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _name;
    private readonly string _commandLine;
    private readonly ScenarioArtifacts _artifacts;
    private readonly ProcessLog _log;
    private readonly E2EEventStream _events = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<E2EResponse>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly Task _exit;
    private long _corruptLines;
    private int? _exitCode;
    private bool _disposing;
    private bool _disposed;

    public E2EEventStream Events => _events;

    public string Name => _name;

    public int ProcessId { get; }

    /// <summary>Redacted; safe for a manifest, a log and an exception.</summary>
    public string CommandLine => _commandLine;

    public bool LogContains(string text) => _log.Contains(text);

    /// <summary>Structured stdout lines that did not parse. Sheared output is
    /// tolerated because the Godot C++ logger shares the handle, but it is
    /// counted and reported.</summary>
    public long CorruptLineCount => Interlocked.Read(ref _corruptLines);

    /// <summary>Cached on exit: the manifest is written after the Process object
    /// is gone, and a report must survive disposal.</summary>
    public int? ExitCode => _exitCode ??= ReadExitCode();

    private E2EProcess(E2EProcessStart start)
    {
        _name = start.Name;
        _artifacts = start.Artifacts;
        _log = new ProcessLog(line => _artifacts.AppendProcess(_name, line));
        _commandLine = CommandLineRedaction.Format(start.FileName, start.Arguments);

        ProcessStartInfo info = new(start.FileName)
        {
            WorkingDirectory = start.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in start.Arguments)
            info.ArgumentList.Add(argument);
        if (start.Environment != null)
        {
            foreach ((string key, string value) in start.Environment)
                info.Environment[key] = value;
        }

        _process = new Process { StartInfo = info };
        try
        {
            if (!_process.Start())
                throw new E2EProcessException($"Failed to start E2E process '{_name}'.");
        }
        catch (Exception exception) when (exception is not E2EProcessException)
        {
            _process.Dispose();
            throw new E2EProcessException(
                $"Failed to start E2E process '{_name}' ({_commandLine}).", exception);
        }

        // Adopted before anything else can go wrong, so a testhost death from
        // here on takes the child with it.
        start.Reaper.Adopt(_process);
        ProcessId = _process.Id;
        _artifacts.Harness($"[{_name}] started pid {ProcessId}: {_commandLine}");

        _stdout = PumpStdoutAsync();
        _stderr = PumpStderrAsync();
        _exit = WatchExitAsync();
    }

    public static E2EProcess Start(E2EProcessStart start) => new(start);

    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : E2ERequest, IE2ERequest<TResponse>
        where TResponse : E2EResponse
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_process.HasExited)
            throw Failure($"Process exited with code {_process.ExitCode} before request {request.Id}.");

        TaskCompletionSource<E2EResponse> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id, completion))
            throw new InvalidOperationException($"Duplicate E2E request id {request.Id}.");

        try
        {
            _artifacts.Harness($"[{_name}] -> {request.GetType().Name} {request.Id}");
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                string line = E2EWire.Serialize(request);
                await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            E2EResponse response;
            try
            {
                response = await completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException exception)
            {
                throw Failure($"Timed out waiting for response to {request.GetType().Name}.", exception);
            }

            _artifacts.Harness($"[{_name}] <- {response.GetType().Name} {response.Id}");
            if (response is CommandFailedResponse failed)
                throw Failure($"{request.GetType().Name} failed: {failed.Error}: {failed.Message}");
            if (response is not TResponse typed)
            {
                throw Failure(
                    $"{request.GetType().Name} expected {typeof(TResponse).Name}, " +
                    $"received {response.GetType().Name}.");
            }
            return typed;
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    public async Task<ProcessReadyEvent> WaitUntilReadyAsync(
        E2EProcessRole role,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ProcessReadyEvent ready = await _events.WaitAsync<ProcessReadyEvent>(
            value => value.Role == role,
            timeout,
            cancellationToken: cancellationToken);
        if (!string.Equals(ready.SchemaHash, E2EProtocolSchema.Hash, StringComparison.Ordinal))
        {
            throw Failure(
                $"Protocol mismatch. Harness={E2EProtocolSchema.Hash}, process={ready.SchemaHash}.");
        }
        return ready;
    }

    public E2EProcessRecord Describe() => new(
        _name, ProcessId, _commandLine, ExitCode, CorruptLineCount, _events.DroppedEventCount);

    /// <summary>Everything an assertion failure needs to be diagnosable without
    /// re-running the scenario.</summary>
    public string Report() =>
        $"Process: {_name} (pid {ProcessId}){Environment.NewLine}" +
        $"Command: {_commandLine}{Environment.NewLine}" +
        $"Exit: {(ExitCode is int code ? code.ToString() : "still running")}{Environment.NewLine}" +
        $"Corrupt lines: {CorruptLineCount}, dropped events: {_events.DroppedEventCount}" +
        $"{Environment.NewLine}Artifacts: {_artifacts.Directory}{Environment.NewLine}" +
        $"Log tail:{Environment.NewLine}{_log.Tail(50)}";

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is string line)
            {
                _log.Add("stdout", line);
                if (!line.StartsWith(E2EWire.STDOUT_PREFIX, StringComparison.Ordinal))
                    continue;
                string payload = line[E2EWire.STDOUT_PREFIX.Length..];
                E2EMessage message;
                try
                {
                    message = E2EWire.DeserializeMessage(payload);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    // Sheared or truncated: counted, reported, never fatal.
                    Interlocked.Increment(ref _corruptLines);
                    _log.Add("corrupt", $"{exception.Message} :: {payload}");
                    continue;
                }

                switch (message)
                {
                    case ResponseMessage response:
                        if (_pending.TryGetValue(response.Response.Id, out
                                TaskCompletionSource<E2EResponse>? completion))
                            completion.TrySetResult(response.Response);
                        else
                            _log.Add("harness", $"orphan response {response.Response.Id}");
                        break;
                    case EventMessage raised:
                        _events.Publish(raised.Event);
                        break;
                }
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync() is string line)
                _log.Add("stderr", line);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private async Task WatchExitAsync()
    {
        await _process.WaitForExitAsync();
        if (!_disposing)
            Fail(Failure($"Process exited unexpectedly with code {_process.ExitCode}."));
    }

    private void Fail(Exception exception)
    {
        foreach (TaskCompletionSource<E2EResponse> completion in _pending.Values)
            completion.TrySetException(exception);
        _events.Fail(exception);
    }

    public E2EProcessException Failure(string message, Exception? inner = null)
    {
        string details = $"{message}{Environment.NewLine}{Report()}";
        return inner == null
            ? new E2EProcessException(details)
            : new E2EProcessException(details, inner);
    }

    /// <summary>Waits the shutdown budget, kills the tree if it lapses, then
    /// drains both pumps. Draining after exit is safe: EOF is guaranteed, and
    /// cancelling first would lose the process's last words.</summary>
    public async Task StopAsync(TimeSpan budget)
    {
        _disposing = true;
        try
        {
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Already gone; the wait below settles it.
        }
        if (!_process.HasExited)
        {
            try
            {
                await _process.WaitForExitAsync().WaitAsync(budget);
            }
            catch (TimeoutException)
            {
                _artifacts.Harness($"[{_name}] shutdown budget lapsed, killing the tree");
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        _exitCode = ReadExitCode();
    }

    private int? ReadExitCode()
    {
        try
        {
            return _process.HasExited ? _process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAsync(TimeSpan.FromSeconds(10));
        await _stdout;
        await _stderr;
        await _exit;
        _artifacts.Harness($"[{_name}] exited with {ExitCode}, " +
                           $"{CorruptLineCount} corrupt line(s), " +
                           $"{_events.DroppedEventCount} dropped event(s)");
        _writeLock.Dispose();
        _process.Dispose();
    }
}
