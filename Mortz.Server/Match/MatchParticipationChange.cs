using Mortz.Core.Match;
using Mortz.Core.Match.Participation;

namespace Mortz.Server.Match;

public readonly record struct MatchParticipationChange(
    int PeerId,
    MatchParticipation State);
