namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Proof that a run is still alive. Held exclusively for the life of the test
/// run and released after the last child exits, so the stale sweep can tell a
/// crashed run's leftovers from a concurrent run's live server: if the lock
/// still opens, nobody owns it.
/// </summary>
public sealed class RunLock : IDisposable
{
    private readonly FileStream _stream;
    private bool _released;

    public string RunId { get; }

    private RunLock(string runId, FileStream stream)
    {
        RunId = runId;
        _stream = stream;
    }

    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "mortz-e2e", "locks");

    public static string PathFor(string runId) => Path.Combine(Root, $"{runId}.lock");

    public static RunLock Acquire(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        Directory.CreateDirectory(Root);
        FileStream stream = new(PathFor(runId), FileMode.Create, FileAccess.ReadWrite,
            FileShare.None, bufferSize: 1, FileOptions.None);
        return new RunLock(runId, stream);
    }

    /// <summary>True when someone still holds the lock. A missing file means the
    /// run is gone, not that it is alive.</summary>
    public static bool IsLive(string runId)
    {
        string path = PathFor(runId);
        if (!File.Exists(path))
            return false;
        try
        {
            using FileStream probe = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static void Forget(string runId)
    {
        try
        {
            File.Delete(PathFor(runId));
        }
        catch (IOException)
        {
            // Someone re-acquired it between the liveness probe and here.
        }
    }

    public void Dispose()
    {
        if (_released)
            return;
        _released = true;
        _stream.Dispose();
        Forget(RunId);
    }
}
