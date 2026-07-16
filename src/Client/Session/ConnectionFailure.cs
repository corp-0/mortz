namespace Mortz.Client.Session;

internal readonly record struct ConnectionFailure(
    ConnectionFailureAction Action,
    int Generation,
    int RetryNumber,
    int MaxRetries);
