using Mortz.Server.Match.Events;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>Turns scored eliminations into game-event judgments.</summary>
public class GameEventsStep(GameEventJudge judge) : IMatchStep
{
    public void Advance(MatchTick tick) =>
        tick.SetGameEvents(judge.JudgeFrame(tick.Eliminations, tick.Match.World.Tick));

    public byte KillingSpreeMagnitude(Player player) =>
        judge.KillingSpreeMagnitude(player);

    public void PlayerLeft(Player player) => judge.PlayerLeft(player);
}
