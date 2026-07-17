namespace Mortz.Core.Net.Messages;

/// <summary>Match entry ticket: which map to load (verified by hash), the
/// match config (MatchConfig blob; prediction must run the server's numbers)
/// and a manifest for the following chunked terrain transfer, so late joiners
/// start in sync without a monolithic RPC.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct WelcomeMsg(
    string MapId,
    string MapHash,
    byte[] Config,
    byte TerrainEncoding,
    int TerrainTransferId,
    int TerrainBytes,
    short TerrainChunks
);
