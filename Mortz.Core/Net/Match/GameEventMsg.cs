using Mortz.Core.Match;
using Mortz.Core.Match.Events;

namespace Mortz.Core.Net.Match;

/// <summary>One server judgment about play, broadcast right after the
/// elimination(s) that caused it. VictimId is 0 when the event has no
/// meaningful victim; Magnitude carries the chain length, streak size or body
/// count where the kind has one. Detail is 0 unless the kind has a use for it:
/// SUICIDE stores the <see cref="SuicideCause"/> there.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct GameEventMsg(
    GameEventKind Kind,
    int ActorId,
    int VictimId,
    byte Magnitude,
    byte Detail = 0
);
