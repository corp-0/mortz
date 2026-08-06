namespace Mortz.Server.Players;

/// <summary>Mints match-lifetime state keys. Framework-constructed, once per
/// match lifetime, and stamped with that lifetime's generation.</summary>
public sealed class MatchStateKeys
{
    private readonly int _generation;
    private int _count;

    public MatchStateKeys(int generation) => _generation = generation;

    public int Count => _count;

    public int Generation => _generation;

    public MatchStateKey<T> Claim<T>() where T : class, new() => new(_count++, _generation);
}
