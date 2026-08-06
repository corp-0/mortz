namespace Mortz.Client.Players;

/// <summary>Mints match-lifetime state keys, once per match open, stamped with
/// that match's generation.</summary>
public sealed class MatchStateKeys(int generation)
{
    public int Count { get; private set; }

    public int Generation => generation;

    public MatchStateKey<T> Claim<T>() where T : class, new() => new(Count++, generation);
}
