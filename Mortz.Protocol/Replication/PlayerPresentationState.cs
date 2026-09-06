using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

/// <summary>Compact server-authored values used only to present a player.</summary>
[NetRow]
public readonly partial record struct PlayerPresentationState(byte KillingSpreeMagnitude, bool IsBleeding);
