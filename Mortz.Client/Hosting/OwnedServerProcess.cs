using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using Mortz.Protocol.Hosting;

namespace Mortz.Client.Hosting;

/// <summary>Owns one child server until it exits, including failed and cancelled launches.</summary>
public class OwnedServerProcess : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly string _pipeName;
    private readonly string? _pipeDirectory;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Process _process;
    private readonly CancellationTokenSource _startup = new();
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _stopTimeout;
    private readonly object _gate = new();
    private Task? _stop;
    private Task? _exit;
    private Task<HostControlMessage>? _handshake;
    private bool _started;

    public OwnedServerProcess(TimeSpan? startupTimeout = null, TimeSpan? stopTimeout = null)
    {
        _startupTimeout = startupTimeout ?? HostControl.StartupTimeout;
        _stopTimeout = stopTimeout ?? HostControl.StopTimeout;
        _pipeName = $"mortz-host-{Guid.NewGuid():N}";
        if (!OperatingSystem.IsWindows())
        {
            _pipeDirectory = Path.Combine(Path.GetTempPath(), _pipeName);
            Directory.CreateDirectory(_pipeDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _pipeName = Path.Combine(_pipeDirectory, "control");
        }
        try
        {
            _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch
        {
            _startup.Dispose();
            if (_pipeDirectory != null)
            {
                Directory.Delete(_pipeDirectory);
            }
            throw;
        }
        _process = new Process();
    }

    public bool HasExited => _exit?.IsCompleted == true;

    public Task<HostControlMessage> StartAsync(ProcessStartInfo startInfo)
    {
        lock (_gate)
        {
            if (_started || _stop != null)
            {
                throw new InvalidOperationException("This server owner has already been used.");
            }
            _started = true;
            startInfo.UseShellExecute = false;
            startInfo.ArgumentList.Add("--host-pipe");
            startInfo.ArgumentList.Add(_pipeName);
            startInfo.ArgumentList.Add("--host-token");
            startInfo.ArgumentList.Add(_token);
            _process.StartInfo = startInfo;
            return StartCoreAsync();
        }
    }

    private async Task<HostControlMessage> StartCoreAsync()
    {
        CancellationToken cancellation = _startup.Token;
        try
        {
            _startup.CancelAfter(_startupTimeout);
            _process.Start();
            _exit = _process.WaitForExitAsync();
            Task<HostControlMessage> handshake = _handshake = HandshakeAsync(cancellation);
            Task completed = await Task.WhenAny(handshake, _exit).ConfigureAwait(false);
            if (completed == _exit && !handshake.IsCompletedSuccessfully && !_pipe.IsConnected)
            {
                int exitCode = _process.ExitCode;
                await StopAsync().ConfigureAwait(false);
                try { await handshake.ConfigureAwait(false); }
                catch (Exception) { /* Observe the cancelled pipe operation before disposing it. */ }
                throw new IOException($"Local server exited before readiness (exit code {exitCode}).");
            }
            HostControlMessage ready;
            try
            {
                ready = await handshake.ConfigureAwait(false);
            }
            catch (EndOfStreamException) when (_exit.IsCompleted)
            {
                throw new IOException($"Local server exited before readiness (exit code {_process.ExitCode}).");
            }
            if (ready.Kind == HostControlKind.FAILED)
            {
                throw new IOException(ready.Reason);
            }
            if (_exit.IsCompleted)
            {
                throw new IOException($"Local server exited before readiness (exit code {_process.ExitCode}).");
            }
            lock (_gate)
            {
                cancellation.ThrowIfCancellationRequested();
                _startup.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            return ready;
        }
        catch (Exception exception)
        {
            bool timedOut = exception is OperationCanceledException && _stop == null;
            await StopAsync().ConfigureAwait(false);
            if (timedOut)
            {
                throw new TimeoutException($"Local server did not become ready within {_startupTimeout.TotalSeconds:g} seconds.", exception);
            }
            throw;
        }
    }

    private async Task<HostControlMessage> HandshakeAsync(CancellationToken cancellation)
    {
        await _pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
        HostControlMessage message = await HostControl.ReadAsync(_pipe, cancellation).ConfigureAwait(false);
        if (!HostControl.MatchesToken(message.Token, _token))
        {
            throw new InvalidDataException("Local server handshake token did not match.");
        }
        if (message.Kind is not (HostControlKind.READY or HostControlKind.FAILED))
        {
            throw new InvalidDataException("Local server did not report readiness.");
        }
        return message;
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            return _stop ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        _startup.Cancel();
        using CancellationTokenSource deadline = new(_stopTimeout);
        try
        {
            if (_exit == null)
            {
                return;
            }
            if (!_exit.IsCompleted && _pipe.IsConnected)
            {
                try
                {
                    await HostControl.WriteAsync(_pipe, new(HostControlKind.STOP, _token), deadline.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    // Closing the pipe also requests shutdown if Stop could not be delivered.
                }
            }
            _pipe.Dispose();
            try
            {
                await _exit.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!_process.HasExited)
                {
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (_process.HasExited)
                    {
                        // The child exited between the liveness check and Kill.
                    }
                }
                await _exit.WaitAsync(_stopTimeout).ConfigureAwait(false);
            }
        }
        finally
        {
            _pipe.Dispose();
            if (_handshake != null)
            {
                try { await _handshake.ConfigureAwait(false); }
                catch (Exception) { /* Startup owns the handshake failure. */ }
            }
            _process.Dispose();
            _startup.Dispose();
            if (_pipeDirectory != null)
            {
                Directory.Delete(_pipeDirectory);
            }
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
