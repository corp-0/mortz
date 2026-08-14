using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;

namespace Mortz.Server.Match;

public readonly record struct FinalKillEvent(
    int Tick,
    ScoredKill Kill,
    Death Death,
    Explosion? Explosion);

/// <summary>Owns the winning transition and victory-lap countdown.</summary>
public class EndingStep(int victoryLapTicks) : IMatchStep
{
    private readonly int _victoryLapTicks = Math.Max(1, victoryLapTicks);
    private int _ticksUntilLobby;

    public Victor? Winner { get; private set; }

    public FinalKillEvent? FinalKill { get; private set; }

    public void Advance(MatchTick tick)
    {
        Victor? matchEnded = null;
        FinalKillEvent? finalKill = null;
        if (tick.WinningScore is WinningScore winningScore)
        {
            ScoredKill elimination = winningScore.Elimination;
            matchEnded = elimination.Score.Winner ??
                throw new InvalidOperationException("A winning score has no winner.");
            BeginVictoryLap(tick.Match, matchEnded);
            finalKill = new FinalKillEvent(
                tick.Match.World.Tick,
                elimination,
                winningScore.Death,
                FindExplosion(winningScore.Death, tick.Explosions));
            FinalKill = finalKill;
        }

        tick.SetEnding(matchEnded, finalKill);
        tick.SetReturnToLobby(false);
    }

    public void BeginVictoryLap(MatchContext match, Victor winner)
    {
        match.Stage = MatchStage.VICTORY_LAP;
        Winner = winner;
        _ticksUntilLobby = _victoryLapTicks;
    }

    public void AdvanceVictoryLap(MatchTick tick)
    {
        if (tick.Match.Stage != MatchStage.VICTORY_LAP)
            throw new InvalidOperationException("The match is not in its victory lap.");

        tick.SetEnding(null, null);
        tick.SetReturnToLobby(--_ticksUntilLobby <= 0);
    }

    private static Explosion? FindExplosion(
        Death death,
        IReadOnlyList<Explosion> explosions)
    {
        Explosion? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Explosion explosion in explosions)
        {
            if (explosion.OwnerId != death.KillerId)
                continue;
            float dx = explosion.X - death.Position.X;
            float dy = explosion.Y - death.Position.Y;
            float distance = dx * dx + dy * dy;
            if (distance >= nearestDistance)
                continue;
            nearest = explosion;
            nearestDistance = distance;
        }

        return nearest;
    }
}
