using Mortz.Server.Players;

namespace Mortz.Server.Phases;

public abstract class ServerPhase : IDisposable
{
    public abstract ServerPhaseKind Kind { get; }

    public abstract IReadOnlyList<object> Services { get; }

    public virtual void OpenPhaseKeys(Player player) { }

    /// <summary>Called once every roster player has this phase's state open. Joins after this use PlayerJoined instead.</summary>
    public abstract void Begin();

    public abstract void PlayerJoined(Player player);

    public abstract void PlayerLeft(Player player);

    public abstract PhaseRequest Advance(ServerTime time);

    public virtual void Inputs(Player player, byte[] packet)
    {
    }

    public virtual void Load(Player player, int generation, bool initialPhase) { }

    public virtual void Dispose() { }
}
