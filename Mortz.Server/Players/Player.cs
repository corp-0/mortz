using Mortz.Server.Phases;

namespace Mortz.Server.Players;

/// <summary>One admitted connection and every service's state about it. State
/// cannot outlive its lifetime and cannot be reached without the player.</summary>
public sealed class Player(
    int peerId,
    string name,
    int serverKeyCount,
    int serverGeneration,
    byte skin = 0)
{
    /// <summary>Neither an issued generation (those start at 1) nor the 0 a
    /// default key carries, so both fail the check instead of one of them
    /// matching a closed phase.</summary>
    private const int CLOSED = -1;

    private readonly object?[] _server = new object?[serverKeyCount];
    private object?[]? _match;
    private int _matchGen = CLOSED;

    public int PeerId { get; } = peerId;

    public string Name { get; } = name;

    public byte Skin { get; } = skin;

    public T State<T>(ServerStateKey<T> key) where T : class, new()
    {
        if (key.Generation != serverGeneration)
            throw new InvalidOperationException($"Stale {typeof(T).Name} state key.");
        return (T)(_server[key.Index] ??= new T());
    }

    public T State<T>(MatchStateKey<T> key) where T : class, new()
    {
        if (key.Generation != _matchGen)
            throw new InvalidOperationException($"Stale {typeof(T).Name} state key.");
        return (T)(_match![key.Index] ??= new T());
    }

    public void OpenMatch(int keyCount, int generation)
    {
        _match = new object?[keyCount];
        _matchGen = generation;
    }

    public void CloseMatch()
    {
        DisposeReverse(_match);
        _match = null;
        _matchGen = CLOSED;
    }

    /// <summary>Disconnect and shutdown: match cells, then server cells, reverse claim order.</summary>
    public void Close(ServerPhaseKind active)
    {
        if (active == ServerPhaseKind.MATCH)
            CloseMatch();
        DisposeReverse(_server);
    }

    private static void DisposeReverse(object?[]? cells)
    {
        if (cells == null)
            return;
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            if (cells[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
