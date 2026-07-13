namespace Mortz.Core.Net.Messages;

/// <summary>Match entry ticket: which map to load (verified by hash), the
/// match config (MatchConfig blob; prediction must run the server's numbers)
/// and the terrain pixels already carved away, so late joiners start in sync.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct WelcomeMsg(string MapId, string MapHash, byte[] Config, byte[] RemovedData);
