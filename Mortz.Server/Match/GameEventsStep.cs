using Mortz.Server.Match.Events;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>Turns scored eliminations into game-event judgments.</summary>
public class GameEventsStep(GameEventJudge judge)
{
    public IReadOnlyList<Judgment> Judge(IReadOnlyList<ScoredKill> eliminations, int tick) =>
        judge.JudgeFrame(eliminations, tick);

    public byte KillingSpreeMagnitude(Player player) =>
        judge.KillingSpreeMagnitude(player);

    public void PlayerLeft(Player player) => judge.PlayerLeft(player);
}
