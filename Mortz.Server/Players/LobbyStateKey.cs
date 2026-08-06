namespace Mortz.Server.Players;

/// <summary>A cell in every player's lobby-lifetime state, stamped with the
/// generation of the claimer that minted it.</summary>
public readonly struct LobbyStateKey<T> where T : class, new()
{
    public readonly int Index;
    public readonly int Generation;

    public LobbyStateKey(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }
}
