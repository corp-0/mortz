namespace Mortz.Core.Match;

/// <summary>Per-player grudge: who killed them last.</summary>
public sealed class RevengeTracker
{
    private readonly Dictionary<int, int> _lastKillerByVictim = new();

    /// <summary>Records the kill and returns true when it settled the
    /// killer's grudge, which is then cleared.</summary>
    public bool OnKill(int killerId, int victimId)
    {
        bool revenge = _lastKillerByVictim.TryGetValue(killerId, out int grudge) &&
                       grudge == victimId;
        if (revenge)
            _lastKillerByVictim.Remove(killerId);
        _lastKillerByVictim[victimId] = killerId;
        return revenge;
    }

    public void RemovePlayer(int peerId)
    {
        _lastKillerByVictim.Remove(peerId);
        foreach (int victim in _lastKillerByVictim
                     .Where(pair => pair.Value == peerId)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _lastKillerByVictim.Remove(victim);
        }
    }
}
