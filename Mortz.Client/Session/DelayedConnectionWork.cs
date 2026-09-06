namespace Mortz.Client.Session;

public class DelayedConnectionWork
{
    private readonly Queue<(ulong Due, Action Run)> _pending = [];

    public void Schedule(ulong due, Action work) => _pending.Enqueue((due, work));

    public void Reset() => _pending.Clear();

    public void Advance(ulong now)
    {
        while (_pending.TryPeek(out (ulong Due, Action Run) next) && next.Due <= now)
        {
            _pending.Dequeue().Run();
        }
    }
}
