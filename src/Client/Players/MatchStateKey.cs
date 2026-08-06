namespace Mortz.Client.Players;

/// <summary>A cell in every player's match-lifetime state, stamped with the
/// generation of the match that minted it.</summary>
public readonly struct MatchStateKey<T> where T : class, new()
{
    public readonly int Index;
    public readonly int Generation;

    public MatchStateKey(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }
}
