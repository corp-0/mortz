using System.Text.Json;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Everything a failed scenario needs, on disk, written on success too. One
/// log per process plus the harness's own typed traffic and a manifest. No
/// secret ever reaches any of them: command lines go through
/// CommandLineRedaction first.
/// </summary>
public sealed class ScenarioArtifacts : IDisposable
{
    private static readonly JsonSerializerOptions _manifestFormat = new() { WriteIndented = true };

    private readonly Dictionary<string, StreamWriter> _processLogs = [];
    private readonly StreamWriter _harness;
    private readonly object _gate = new();
    private bool _disposed;

    public string Directory { get; }

    private ScenarioArtifacts(string directory)
    {
        Directory = directory;
        _harness = Open("harness");
    }

    public static ScenarioArtifacts Create(string runId, string scenario)
    {
        string root = Environment.GetEnvironmentVariable("MORTZ_E2E_ARTIFACTS")
                      ?? Path.Combine(RepoRoot.Path, "build", "e2e");
        string directory = Path.Combine(root, runId, Sanitize(scenario));
        System.IO.Directory.CreateDirectory(directory);
        return new ScenarioArtifacts(directory);
    }

    public void AppendProcess(string processName, ProcessLogLine line)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (!_processLogs.TryGetValue(processName, out StreamWriter? writer))
            {
                writer = Open(processName);
                _processLogs[processName] = writer;
            }
            writer.WriteLine($"{line.At:O} [{line.Stream}] {line.Text}");
        }
    }

    public void Harness(string text)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _harness.WriteLine($"{DateTimeOffset.UtcNow:O} {text}");
        }
    }

    public void WriteManifest(E2EManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        File.WriteAllText(
            Path.Combine(Directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, _manifestFormat));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (StreamWriter writer in _processLogs.Values)
            {
                writer.Dispose();
            }
            _processLogs.Clear();
            _harness.Dispose();
        }
    }

    private StreamWriter Open(string name) =>
        new(Path.Combine(Directory, $"{Sanitize(name)}.log"), append: true) { AutoFlush = true };

    private static string Sanitize(string name) =>
        string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
