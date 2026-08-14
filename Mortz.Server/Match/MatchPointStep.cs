using Mortz.Core.Match.Scoring;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match;

/// <summary>Held is the new state, null when match point just lapsed.</summary>
public readonly record struct MatchPointChange(MatchPoint? Held);

/// <summary>Owns match-point state and emits only its transitions.</summary>
public class MatchPointStep : IMatchStep
{
    private const int MATCH_POINT_REMAINING = 1;

    public MatchPoint? Active { get; private set; }

    public void Advance(MatchTick tick)
    {
        MatchStanding standing = tick.Standing;
        bool active = tick.Match.Stage == MatchStage.PLAYING &&
                      standing.Remaining == MATCH_POINT_REMAINING;
        if (active == (Active != null))
        {
            tick.SetMatchPoint(null);
            return;
        }

        Active = active ? new MatchPoint(standing.Remaining, standing.Leader) : null;
        tick.SetMatchPoint(new MatchPointChange(Active));
    }
}
