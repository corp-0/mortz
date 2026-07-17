namespace Mortz.Server;

/// <summary>Process-lifetime win tallies. A player's count survives lobby and
/// match swaps but dies with their connection or the server process.</summary>
internal sealed class WinTracker
{
    private readonly SortedDictionary<long, int> _wins = new();

    public int Wins(long peerId) => _wins.GetValueOrDefault(peerId);

    public void RecordWin(long peerId) => _wins[peerId] = Wins(peerId) + 1;

    public void Remove(long peerId) => _wins.Remove(peerId);
}
