using Mortz.Core.Match;
using Mortz.Core.Match.Events;

namespace Mortz.Server.Match.Events;

/// <summary>VictimId is 0 when the event has no meaningful victim; Detail
/// is 0 unless the kind has a use for it (SUICIDE stores the cause).</summary>
public readonly record struct Judgment(
    GameEventKind Kind,
    int ActorId,
    int VictimId,
    byte Magnitude,
    byte Detail = 0);
