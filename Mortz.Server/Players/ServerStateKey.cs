namespace Mortz.Server.Players;

/// <summary>A cell in every player's server-lifetime state, stamped with the
/// generation of the claimer that minted it.</summary>
public readonly struct ServerStateKey<T>(int index, int generation)
    where T : class, new()
{
    public readonly int Index = index;
    public readonly int Generation = generation;
}
