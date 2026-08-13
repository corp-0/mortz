using Mortz.Core.Match.Participation;

namespace Mortz.Core.Net;

/// <summary>Starts loading the lobby.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbyLoadMsg(int Generation);

/// <summary>Starts loading a match with its map, rules, terrain, participation, and initial state.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchLoadMsg(
    string MapId,
    string MapHash,
    byte[] Config,
    byte TerrainEncoding,
    int TerrainTransferId,
    int TerrainBytes,
    short TerrainChunks,
    MatchSeat Seat,
    MatchActivity Activity,
    SpectateReason SpectateReason,
    int ReturnTick,
    byte[] InitialSnapshot,
    int InitialSnapshotAck,
    int Generation = 0
);

/// <summary>The phase screen and its handlers are ready.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct PhaseReadyMsg(int Generation);

/// <summary>Starts prediction and the match after every lobby player loads.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchStartMsg(int Generation);
