namespace Mortz.Server.Session;

/// <summary>Process-lifetime win tallies. A player's count survives lobby and
/// match swaps but dies with their connection or the server process.</summary>
internal sealed class WinTracker
{
    private readonly SortedDictionary<int, int> _wins = new();

    public int Wins(int peerId) => _wins.GetValueOrDefault(peerId);

    public void RecordWin(int peerId) => _wins[peerId] = Wins(peerId) + 1;

    public void Remove(int peerId) => _wins.Remove(peerId);
}
