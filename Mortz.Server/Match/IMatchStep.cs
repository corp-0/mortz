namespace Mortz.Server.Match;

/// <summary>One ordered stage of normal match advancement.</summary>
public interface IMatchStep
{
    void Advance(MatchTick tick);
}
