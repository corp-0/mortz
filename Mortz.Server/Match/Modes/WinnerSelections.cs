using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match.Modes;

public interface IWinnerSelection
{
    Victor? Select(IReadOnlyList<ContenderScore> scores, EndDecision decision);
}

public static class WinnerSelections
{
    public static IWinnerSelection Create(WinnerRule rule) => rule switch
    {
        WinnerRule.HIGHEST_SCORE => new RankedWinner(highest: true),
        WinnerRule.LOWEST_SCORE => new RankedWinner(highest: false),
        _ => throw new ArgumentOutOfRangeException(nameof(rule)),
    };
}

/// <summary>Tied scores leave the match running until there is one winner.</summary>
public class RankedWinner(bool highest) : IWinnerSelection
{
    public Victor? Select(IReadOnlyList<ContenderScore> scores, EndDecision decision)
    {
        ContenderScore? best = null;
        bool tied = false;
        foreach (ContenderScore score in scores)
        {
            if (best == null || (highest ? score.Value > best.Value.Value : score.Value < best.Value.Value))
            {
                best = score;
                tied = false;
            }
            else if (score.Value == best.Value.Value)
            {
                tied = true;
            }
        }
        return tied ? null : best?.Contender;
    }
}

public class QualifyingWinner : IWinnerSelection
{
    public Victor? Select(IReadOnlyList<ContenderScore> scores, EndDecision decision) => decision.Qualifier;
}
