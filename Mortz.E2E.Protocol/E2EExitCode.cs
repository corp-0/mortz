namespace Mortz.E2E.Protocol;

public static class E2EExitCode
{
    public const int CLEAN = 0;

    /// <summary>Stdin closed after the driver was armed. The harness died.</summary>
    public const int STDIN_EOF = 64;
}
