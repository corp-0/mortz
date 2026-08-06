using System.Diagnostics;
using Mortz.E2E.Protocol;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// One process's events, with history so "it arrived before I waited" is not a
/// race. Every event carries a monotonic sequence, and a wait can be anchored
/// on a cursor: a loose predicate then cannot match something that happened
/// before the caller asked for it, which is the silent-false-green failure
/// mode this exists to remove.
/// </summary>
public sealed class E2EEventStream
{
    private const int MAX_HISTORY = 4096;

    private readonly List<RecordedEvent> _history = [];
    private readonly object _gate = new();
    private TaskCompletionSource _changed = NewSignal();
    private Exception? _failure;
    private long _nextSequence = 1;
    private long _dropped;

    /// <summary>The newest event's position; a wait anchored here ignores
    /// everything already published.</summary>
    public E2EEventCursor Cursor
    {
        get
        {
            lock (_gate)
            {
                return new E2EEventCursor(_nextSequence - 1);
            }
        }
    }

    /// <summary>Events pushed out of the bounded history.</summary>
    public long DroppedEventCount
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    public void Publish(E2EEvent value)
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            if (_failure != null)
                return;
            _history.Add(new RecordedEvent(_nextSequence++, value));
            if (_history.Count > MAX_HISTORY)
            {
                int excess = _history.Count - MAX_HISTORY;
                _history.RemoveRange(0, excess);
                _dropped += excess;
            }
            changed = _changed;
            _changed = NewSignal();
        }
        changed.TrySetResult();
    }

    public void Fail(Exception exception)
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            if (_failure != null)
                return;
            _failure = exception;
            changed = _changed;
            _changed = NewSignal();
        }
        changed.TrySetResult();
    }

    public async Task<TEvent> WaitAsync<TEvent>(
        Func<TEvent, bool> predicate,
        TimeSpan timeout,
        E2EEventCursor after = default,
        CancellationToken cancellationToken = default)
        where TEvent : E2EEvent
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_failure != null)
                    throw new E2EProcessException("The E2E event stream failed.", _failure);
                RequireReachable(after);
                foreach (RecordedEvent recorded in _history)
                {
                    if (recorded.Sequence <= after.Sequence)
                        continue;
                    if (recorded.Event is TEvent typed && predicate(typed))
                        return typed;
                }
                changed = _changed.Task;
            }

            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"No matching {typeof(TEvent).Name} arrived in {timeout}.");
            await changed.WaitAsync(remaining, cancellationToken);
        }
    }

    /// <summary>Silently starting the scan later than asked would be the same
    /// vacuous match the cursor exists to prevent, so it is an error.</summary>
    private void RequireReachable(E2EEventCursor after)
    {
        long firstRetained = _history.Count > 0 ? _history[0].Sequence : _nextSequence;
        if (after.Sequence + 1 >= firstRetained)
            return;
        throw new E2EProcessException(
            $"Cursor {after.Sequence} is older than the retained history " +
            $"(first retained {firstRetained}, {_dropped} event(s) dropped).");
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
