namespace Mortz.Protocol.Net.Sim;

/// <summary>Packed live-shell corrections</summary>
[NetMessage(NetChannel.UNRELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MortarCorrectionMsg(int Tick, byte[] States, int MatchGeneration = 0);

/// <summary>Ordered shell spawns, parries, and endings from one simulation tick.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MortarLifecycleMsg(byte[] Events, int MatchGeneration = 0);

/// <summary>Retires a predicted shell after deflection without waiting for an unreliable snapshot.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ShellRetireMsg(int SpawnSeq, int MatchGeneration = 0);
