namespace Mortz.Client.Session;

public readonly record struct ConnectionFailure(
    ConnectionFailureAction Action,
    int Generation,
    int RetryNumber,
    int MaxRetries);
