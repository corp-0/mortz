using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match.Scoring.WinConditions;

/// <summary>Tracks the best and second-best score offered, so the standing is
/// the lead the front-runner holds over whoever chases them.</summary>
public sealed class LeadRace(int target)
{
    private int _best = int.MinValue;
    private int _second = int.MinValue;
    private int _count;
    private Victor? _leader;
    private bool _tied;

    public void Offer(Victor contender, int score)
    {
        _count++;
        if (score > _best)
        {
            _second = _best;
            _best = score;
            _leader = contender;
            _tied = false;
        }
        else if (score == _best)
        {
            _second = _best;
            _tied = true;
        }
        else if (score > _second)
        {
            _second = score;
        }
    }

    public MatchStanding Standing()
    {
        if (_count < 2 || _tied)
            return new MatchStanding(null, target);
        long lead = (long)_best - _second;
        return new MatchStanding(_leader, (int)Math.Max(0, target - lead));
    }
}
