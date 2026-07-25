using Mortz.Core.Sim;

namespace Mortz.Core.Match;

/// <summary>Consecutive-kill chains inside a short window, per player. A chain
/// survives the killer's own death: a shell already in flight still counts.</summary>
public sealed class MultiKillTracker
{
    public const float WINDOW_SECONDS = 3f;
    public const int WINDOW_TICKS = (int)(WINDOW_SECONDS * SimConfig.TICK_RATE);

    private readonly Dictionary<int, (int Chain, int Tick)> _chains = new();

    /// <summary>Returns the chain length including this kill.</summary>
    public int OnKill(int killerId, int tick)
    {
        int chain = 1;
        if (_chains.TryGetValue(killerId, out (int Chain, int Tick) last) &&
            tick - last.Tick <= WINDOW_TICKS)
            chain = last.Chain + 1;
        _chains[killerId] = (chain, tick);
        return chain;
    }

    public void RemovePlayer(int peerId) => _chains.Remove(peerId);
}
