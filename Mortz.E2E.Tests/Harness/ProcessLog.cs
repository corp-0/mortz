using System.Text;

namespace Mortz.E2E.Tests.Harness;

/// <summary>The bounded tail an exception message can carry. Every line also
/// goes to the artifact sink, so the disk copy is never truncated.</summary>
public sealed class ProcessLog(Action<ProcessLogLine>? sink = null, int capacity = 300)
{
    private readonly Queue<ProcessLogLine> _lines = new();
    private readonly object _gate = new();

    public void Add(string stream, string line)
    {
        ProcessLogLine recorded = new(DateTimeOffset.UtcNow, stream, line);
        lock (_gate)
        {
            _lines.Enqueue(recorded);
            while (_lines.Count > capacity)
                _lines.Dequeue();
        }
        sink?.Invoke(recorded);
    }

    public string Tail(int lines = int.MaxValue)
    {
        lock (_gate)
        {
            StringBuilder text = new();
            foreach (ProcessLogLine line in _lines.Skip(Math.Max(0, _lines.Count - lines)))
            {
                text.Append(line.At.ToString("O"))
                    .Append(' ')
                    .Append('[').Append(line.Stream).Append("] ")
                    .AppendLine(line.Text);
            }
            return text.ToString();
        }
    }

    public bool Contains(string text)
    {
        lock (_gate)
        {
            return _lines.Any(line => line.Text.Contains(text, StringComparison.Ordinal));
        }
    }
}
