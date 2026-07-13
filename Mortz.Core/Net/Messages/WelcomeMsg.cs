namespace Mortz.Core.Net.Messages;

/// <summary>Match entry ticket: which map to load (verified by hash) plus the
/// terrain pixels already carved away, so late joiners start in sync.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct WelcomeMsg(string MapId, string MapHash, byte[] RemovedData);
