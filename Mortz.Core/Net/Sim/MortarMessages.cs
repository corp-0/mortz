namespace Mortz.Core.Net.Sim;

/// <summary>Packed live-shell corrections</summary>
[NetMessage(NetChannel.UNRELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MortarCorrectionMsg(int Tick, byte[] States);

/// <summary>Ordered shell spawns, parries, and endings from one simulation tick.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MortarLifecycleMsg(byte[] Events);

/// <summary>Retires a predicted shell after deflection without waiting for an unreliable snapshot.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ShellRetireMsg(int SpawnSeq);
