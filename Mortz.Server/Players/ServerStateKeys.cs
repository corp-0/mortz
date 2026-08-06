namespace Mortz.Server.Players;

/// <summary>Mints server-lifetime state keys. Framework-constructed, once per
/// server lifetime, and stamped with that lifetime's generation.</summary>
public sealed class ServerStateKeys(int generation)
{
    public int Count { get; private set; }

    public int Generation => generation;

    public ServerStateKey<T> Claim<T>() where T : class, new() => new(Count++, generation);
}
