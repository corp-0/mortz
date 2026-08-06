namespace Mortz.Client.Score;

/// <summary>A player's kill and death counts as last told by the server.</summary>
public sealed class ScoreState
{
    public int Kills { get; set; }
    public int Deaths { get; set; }
}
