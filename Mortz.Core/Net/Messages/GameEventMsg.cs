using Mortz.Core.Match;

namespace Mortz.Core.Net.Messages;

/// <summary>One server judgment about play, broadcast right after the
/// elimination(s) that caused it. VictimId is 0 when the event has no
/// meaningful victim; Magnitude carries the chain length, streak size or body
/// count where the kind has one.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct GameEventMsg(
    GameEventKind Kind,
    long ActorId,
    long VictimId,
    byte Magnitude);
