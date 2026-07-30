namespace Mortz.Core.Net;

/// <summary>The options a lobby offers on one axis, compared by value so a
/// re-sent identical catalog is not a change.</summary>
public sealed class LobbyCatalog : IEquatable<LobbyCatalog>
{
    public static readonly LobbyCatalog EMPTY = new([]);

    private readonly ContentOption[] _options;

    public LobbyCatalog(IEnumerable<ContentOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.ToArray();
    }

    public IReadOnlyList<ContentOption> Options => _options;

    public bool Equals(LobbyCatalog? other) =>
        other is not null &&
        (ReferenceEquals(this, other) || _options.AsSpan().SequenceEqual(other._options));

    public override bool Equals(object? obj) => Equals(obj as LobbyCatalog);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (ContentOption option in _options)
        {
            hash.Add(option);
        }
        return hash.ToHashCode();
    }
}
