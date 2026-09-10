using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;

namespace Mortz.Core.Match.Respawning;

public abstract class RespawnStrategy
{
    public static RespawnStrategy Create(RespawnRules rules) => rules switch
    {
        NoRespawnRules => new NoRespawnStrategy(),
        FixedRespawnRules fixedRules => new FixedRespawnStrategy(fixedRules.DelayTicks),
        _ => throw new ArgumentOutOfRangeException(nameof(rules)),
    };

    public virtual void Entered(int peerId, Team? team) { }
    public abstract void Died(Death death, int tick);

    /// <summary>Null blocks respawning; a tick at or before now permits it. Polling must not change strategy state.</summary>
    public abstract int? ReturnTick(int peerId, int tick);

    /// <summary>Called after installing the body, before checking the next player. Must not throw.</summary>
    public virtual void Spawned(int peerId, bool initial) { }
    public virtual void Removed(int peerId) { }
}

public class FixedRespawnStrategy(int delayTicks) : RespawnStrategy
{
    private readonly Dictionary<int, int> _returns = [];

    public override void Died(Death death, int tick) =>
        _returns.Add(death.PeerId, checked(tick + Math.Max(1, delayTicks)));

    public override int? ReturnTick(int peerId, int tick) =>
        _returns.TryGetValue(peerId, out int at) ? at : null;

    public override void Spawned(int peerId, bool initial) => _returns.Remove(peerId);
    public override void Removed(int peerId) => _returns.Remove(peerId);
}

public class NoRespawnStrategy : RespawnStrategy
{
    public override void Died(Death death, int tick) { }

    public override int? ReturnTick(int peerId, int tick) => null;
}
