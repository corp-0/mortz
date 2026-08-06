namespace Mortz.Client.Players;

/// <summary>Mints connection-lifetime state keys, once per ClientPlayers
/// registry, stamped with that registry's generation.</summary>
public sealed class SessionStateKeys(int generation)
{
    public int Count { get; private set; }

    public int Generation => generation;

    public SessionStateKey<T> Claim<T>() where T : class, new() => new(Count++, generation);
}
