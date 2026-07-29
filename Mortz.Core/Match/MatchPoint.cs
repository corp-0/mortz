namespace Mortz.Core.Match;

/// <summary>Someone is Remaining kills from winning. Leader is null when
/// nobody has a meaningful lead, as a kill target of 1 does on an empty
/// board.</summary>
public sealed record MatchPoint
{
    public MatchPoint(int remaining, Victor? leader)
    {
        if (remaining < 1)
            throw new ArgumentOutOfRangeException(nameof(remaining),
                "Zero remaining is a won match, not match point.");
        Remaining = remaining;
        Leader = leader;
    }

    public int Remaining { get; }
    public Victor? Leader { get; }
}
