namespace Mortz.Client;

internal enum ConnectionFailureAction
{
    Ignore,
    Retry,
    Failed,
}

internal readonly record struct ConnectionFailure(
    ConnectionFailureAction Action,
    int Generation,
    int RetryNumber,
    int MaxRetries);

/// <summary>One logical connection attempt. Generations make delayed retries
/// harmless after a new attempt, successful connection, or disconnect.</summary>
internal sealed class ClientConnectionAttempt
{
    private readonly int _maxRetries;
    private int _generation;
    private int _retriesLeft;
    private bool _active;
    private bool _retryScheduled;

    public string Address { get; private set; } = "";
    public int Port { get; private set; }
    public string PlayerName { get; private set; } = "";

    public ClientConnectionAttempt(int maxRetries) =>
        _maxRetries = Math.Max(0, maxRetries);

    public void Start(string address, int port, string playerName)
    {
        _generation++;
        Address = address;
        Port = port;
        PlayerName = playerName;
        _retriesLeft = _maxRetries;
        _retryScheduled = false;
        _active = true;
    }

    public ConnectionFailure Failed()
    {
        if (!_active || _retryScheduled)
            return new ConnectionFailure(ConnectionFailureAction.Ignore, _generation, 0, _maxRetries);
        if (_retriesLeft-- > 0)
        {
            _retryScheduled = true;
            return new ConnectionFailure(ConnectionFailureAction.Retry, _generation,
                _maxRetries - _retriesLeft, _maxRetries);
        }
        _active = false;
        return new ConnectionFailure(ConnectionFailureAction.Failed, _generation,
            _maxRetries, _maxRetries);
    }

    public bool BeginScheduledRetry(int generation)
    {
        if (!_active || !_retryScheduled || generation != _generation)
            return false;
        _retryScheduled = false;
        return true;
    }

    public void Connected()
    {
        _active = false;
        _retryScheduled = false;
    }

    public void Cancel()
    {
        _generation++;
        _active = false;
        _retryScheduled = false;
    }
}
