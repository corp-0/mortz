using Mortz.Core.Match;
using Mortz.Core.Sim;

namespace Mortz.Server.Match;

public readonly record struct FinalKillEvent(
    int Tick,
    ScoredKill Kill,
    Death Death,
    Explosion? Explosion);
