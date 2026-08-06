using Mortz.Core.Match;
using Mortz.Core.Match.Participation;

namespace Mortz.Core.Net.Match;

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchParticipationMsg(
    MatchSeat Seat,
    MatchActivity Activity,
    SpectateReason Reason,
    int ReturnTick);
