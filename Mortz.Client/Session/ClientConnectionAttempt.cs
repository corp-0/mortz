namespace Mortz.Client.Session;

public readonly record struct ConnectionFailure(
    ConnectionFailureAction Action,
    int Generation,
    int RetryNumber,
    int MaxRetries);

/// <summary>Tracks one connection attempt and its retry budget.
/// Generation tokens prevent scheduled retries from starting after the
/// attempt has been replaced, connected, or cancelled.</summary>
public sealed class ClientConnectionAttempt(int maxRetries)
{
    private readonly int _maxRetries = Math.Max(0, maxRetries);
    private int _generation;
    private int _retriesLeft;
    private bool _active;
    private bool _retryScheduled;

    public string Address { get; private set; } = "";
    public int Port { get; private set; }
    public string PlayerName { get; private set; } = "";
    public int Skin { get; private set; }

    public void Start(string address, int port, string playerName, int skin = 0)
    {
        _generation++;
        Address = address;
        Port = port;
        PlayerName = playerName;
        Skin = skin;
        _retriesLeft = _maxRetries;
        _retryScheduled = false;
        _active = true;
    }

    public ConnectionFailure Failed()
    {
        if (!_active || _retryScheduled)
            return new ConnectionFailure(ConnectionFailureAction.IGNORE, _generation, 0, _maxRetries);
        if (_retriesLeft-- > 0)
        {
            _retryScheduled = true;
            return new ConnectionFailure(ConnectionFailureAction.RETRY, _generation,
                _maxRetries - _retriesLeft, _maxRetries);
        }
        _active = false;
        return new ConnectionFailure(ConnectionFailureAction.FAILED, _generation,
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
