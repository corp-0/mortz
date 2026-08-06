namespace Mortz.Core.Net.Sim;

/// <summary>One reliable, ordered batch of shell spawns, parries, and ends
/// produced by a simulation tick.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MortarLifecycleMsg(byte[] Events);
