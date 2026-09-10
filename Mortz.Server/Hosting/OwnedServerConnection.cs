using System.IO.Pipes;
using Mortz.Protocol.Hosting;

namespace Mortz.Server.Hosting;

public class OwnedServerConnection : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly string _token;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _connected;
    private readonly Task _commands;
    private int _stopRequested;
    private int _disposed;
    private Task? _report;

    public bool StopRequested => Volatile.Read(ref _stopRequested) != 0;

    public OwnedServerConnection(string pipeName, string token)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || token.Length != 64 || !token.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Invalid local hosting control arguments.");
        }
        _token = token;
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _connected = _pipe.ConnectAsync((int)HostControl.StartupTimeout.TotalMilliseconds, _lifetime.Token);
        _commands = WatchParentAsync();
    }

    public Task ReportAsync(HostControlMessage message) => _report = ReportCoreAsync(message);

    private async Task ReportCoreAsync(HostControlMessage message)
    {
        try
        {
            await _connected.ConfigureAwait(false);
            await HostControl.WriteAsync(_pipe, message with { Token = _token }, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            Interlocked.Exchange(ref _stopRequested, 1);
        }
    }

    private async Task WatchParentAsync()
    {
        try
        {
            await _connected.ConfigureAwait(false);
            HostControlMessage command = await HostControl.ReadAsync(_pipe, _lifetime.Token).ConfigureAwait(false);
            if (!HostControl.MatchesToken(command.Token, _token) || command.Kind != HostControlKind.STOP)
            {
                throw new InvalidDataException("Invalid local host command.");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or OperationCanceledException or ObjectDisposedException or FormatException or ArgumentException)
        {
            // An owned server must not survive loss of its control channel.
        }
        finally
        {
            Interlocked.Exchange(ref _stopRequested, 1);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _lifetime.Cancel();
        _pipe.Dispose();
        _commands.GetAwaiter().GetResult();
        _report?.GetAwaiter().GetResult();
        _lifetime.Dispose();
    }
}
