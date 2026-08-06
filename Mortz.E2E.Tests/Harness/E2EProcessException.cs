namespace Mortz.E2E.Tests.Harness;

public sealed class E2EProcessException : Exception
{
    public E2EProcessException(string message) : base(message) { }

    public E2EProcessException(string message, Exception innerException)
        : base(message, innerException) { }
}
