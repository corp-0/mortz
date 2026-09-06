namespace Mortz.Server.Players;

/// <summary>Mints server-lifetime state keys. Framework-constructed, once per
/// server lifetime, and stamped with that lifetime's generation.</summary>
public sealed class ServerStateKeys(int generation)
{
    public int Count { get; private set; }

    public int Generation => generation;

    private bool _sealed;
    private readonly List<(Type Feature, Type State)> _owners = [];
    public string Describe() => string.Join(", ", _owners.Select(owner =>
        $"{owner.Feature.Name}: {owner.State.Name}"));
    public void Seal() => _sealed = true;

    public ServerStateKey<T> Claim<T>(Type? feature = null) where T : class, new()
    {
        if (_sealed)
            throw new InvalidOperationException("Player state registration is closed.");
        _owners.Add((feature ?? typeof(T), typeof(T)));
        return new(Count++, generation);
    }
}
