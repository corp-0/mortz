using Mortz.Core.Sim;

namespace Mortz.Core.Input;

/// <summary>
/// Client-side record of recently sent inputs, kept until the server
/// acknowledges applying them. Reconciliation replays everything newer than
/// the ack; redundant sending re-transmits the newest few each packet.
/// Sequence numbers are the client's own tick counter and strictly increase.
/// </summary>
public sealed class InputHistory
{
    private const int MAX_KEPT = 128;

    private readonly List<(int Seq, PlayerInput Input)> _items = new();

    public void Add(int seq, PlayerInput input)
    {
        _items.Add((seq, input));
        if (_items.Count > MAX_KEPT)
            _items.RemoveAt(0);
    }

    public bool TryGet(int seq, out PlayerInput input)
    {
        foreach ((int Seq, PlayerInput Input) item in _items)
        {
            if (item.Seq == seq)
            {
                input = item.Input;
                return true;
            }
        }
        input = default;
        return false;
    }

    /// <summary>All stored inputs with sequence strictly greater than <paramref name="seq"/>, in order.</summary>
    public IEnumerable<(int Seq, PlayerInput Input)> Since(int seq)
    {
        foreach ((int Seq, PlayerInput Input) item in _items)
            if (item.Seq > seq)
                yield return item;
    }

    /// <summary>The newest <paramref name="n"/> inputs (fewer if not enough stored), in order.</summary>
    public IReadOnlyList<(int Seq, PlayerInput Input)> Newest(int n)
    {
        int start = Math.Max(0, _items.Count - n);
        return _items.GetRange(start, _items.Count - start);
    }

    /// <summary>Drops everything older than <paramref name="seq"/>, keeping the
    /// acked input itself: a later snapshot may repeat the same ack (server
    /// starvation), and reconciliation needs it as the PrevButtons anchor.</summary>
    public void DropThrough(int seq) => _items.RemoveAll(i => i.Seq < seq);
}
