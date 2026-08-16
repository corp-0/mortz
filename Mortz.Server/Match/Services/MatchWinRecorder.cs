using Mortz.Core.Match.Scoring;
using Mortz.Server.Wins;

namespace Mortz.Server.Match.Services;

public class MatchWinRecorder(MatchRuntime runtime, WinsService wins) : IObserveMatchUpdate
{
    public void MatchUpdated(in MatchUpdate update, ServerTime time)
    {
        if (update.MatchEnded is Victor winner) wins.Record(runtime.Winners(winner));
    }
}
