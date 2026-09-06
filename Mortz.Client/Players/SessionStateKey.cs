namespace Mortz.Client.Players;

/// <summary>A cell in every player's connection-lifetime state, stamped with
/// the generation of the registry that minted it.</summary>
public readonly struct SessionStateKey<T> where T : class, new()
{
    public readonly int Index;
    public readonly int Generation;

    public SessionStateKey(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }
}
