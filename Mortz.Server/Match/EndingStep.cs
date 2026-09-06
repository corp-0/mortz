using Mortz.Core.Match.Scoring;
using Mortz.Server.Match.Modes;

namespace Mortz.Server.Match;

/// <summary>Owns the winning transition and victory-lap countdown.</summary>
public class EndingStep(int victoryLapTicks)
{
    private readonly int _victoryLapTicks = Math.Max(1, victoryLapTicks);
    private int _ticksUntilLobby;

    public Victor? Winner { get; private set; }

    public FinalKillEvent? FinalKill { get; private set; }

    public EndingOutput Apply(MatchContext match, MatchOutcome? outcome)
    {
        if (outcome == null)
            return default;
        BeginVictoryLap(match, outcome.Winner);
        FinalKill = outcome.FinalKill;
        return new EndingOutput(outcome.Winner, outcome.FinalKill);
    }

    public void BeginVictoryLap(MatchContext match, Victor winner)
    {
        match.Stage = MatchStage.VICTORY_LAP;
        Winner = winner;
        _ticksUntilLobby = _victoryLapTicks;
    }

    public bool AdvanceVictoryLap(MatchContext match)
    {
        if (match.Stage != MatchStage.VICTORY_LAP)
            throw new InvalidOperationException("The match is not in its victory lap.");

        return --_ticksUntilLobby <= 0;
    }
}

public readonly record struct EndingOutput(Victor? Winner, FinalKillEvent? FinalKill);
