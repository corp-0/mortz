namespace Mortz.Protocol.Net.Sim;

/// <summary>OwnerId and SpawnSeq identify a predicted shell; 0/-1 marks an unpredicted carve.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct CarveMsg(short X, short Y, byte Radius, int OwnerId, int SpawnSeq, int Tick = 0, int ShellId = -1, int MatchGeneration = 0);

/// <summary>X and Y are the body center at death.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct DeathMsg(int PeerId, short X, short Y, int Tick = 0, int ShellId = -1, int MatchGeneration = 0);
