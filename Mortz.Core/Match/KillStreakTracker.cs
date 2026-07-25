namespace Mortz.Core.Match;

/// <summary>Kills since last death, per player.</summary>
public sealed class KillStreakTracker
{
    private readonly Dictionary<int, int> _streaks = new();

    /// <summary>Returns the killer's streak including this kill.</summary>
    public int OnKill(int killerId)
    {
        _streaks.TryGetValue(killerId, out int streak);
        _streaks[killerId] = ++streak;
        return streak;
    }

    /// <summary>Returns the streak the victim died with.</summary>
    public int OnDeath(int victimId)
    {
        _streaks.Remove(victimId, out int streak);
        return streak;
    }

    public void RemovePlayer(int peerId) => _streaks.Remove(peerId);
}
