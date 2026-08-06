namespace Mortz.Server.Players;

/// <summary>Mints lobby-lifetime state keys. Framework-constructed, once per
/// lobby lifetime, and stamped with that lifetime's generation.</summary>
public sealed class LobbyStateKeys
{
    private readonly int _generation;
    private int _count;

    public LobbyStateKeys(int generation) => _generation = generation;

    public int Count => _count;

    public int Generation => _generation;

    public LobbyStateKey<T> Claim<T>() where T : class, new() => new(_count++, _generation);
}
