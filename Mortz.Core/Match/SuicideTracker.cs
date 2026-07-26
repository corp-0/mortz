using Mortz.Core.Sim;

namespace Mortz.Core.Match;

/// <summary>Consecutive self-inflicted deaths per player within a sliding
/// window; a credited kill resets the count.</summary>
public sealed class SuicideTracker
{
    public const float WINDOW_SECONDS = 30f;
    public const int WINDOW_TICKS = (int)(WINDOW_SECONDS * SimConfig.TICK_RATE);

    private readonly Dictionary<int, (int Count, int Tick)> _counts = new();

    /// <summary>Returns the consecutive count including this suicide.</summary>
    public int OnSuicide(int peerId, int tick)
    {
        int count = 1;
        if (_counts.TryGetValue(peerId, out (int Count, int Tick) last) &&
            tick - last.Tick <= WINDOW_TICKS)
            count = last.Count + 1;
        _counts[peerId] = (count, tick);
        return count;
    }

    public void OnCreditedKill(int killerId) => _counts.Remove(killerId);

    public void RemovePlayer(int peerId) => _counts.Remove(peerId);
}
