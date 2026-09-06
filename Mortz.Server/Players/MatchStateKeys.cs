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

    private bool _sealed;
    private readonly List<(Type Feature, Type State)> _owners = [];
    public string Describe() => string.Join(", ", _owners.Select(owner =>
        $"{owner.Feature.Name}: {owner.State.Name}"));
    public void Seal() => _sealed = true;

    public MatchStateKey<T> Claim<T>(Type? feature = null) where T : class, new()
    {
        if (_sealed)
            throw new InvalidOperationException("Player state registration is closed.");
        _owners.Add((feature ?? typeof(T), typeof(T)));
        return new(_count++, _generation);
    }
}
