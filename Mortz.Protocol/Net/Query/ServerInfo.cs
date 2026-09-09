namespace Mortz.Protocol.Net.Query;

/// <summary>What a server tells the browser about itself. The version fields
/// are there so an unjoinable server reads as incompatible rather than dead.</summary>
public record ServerInfo(
    string Name,
    string Mode,
    string Map,
    int Players,
    int MaxPlayers,
    bool InLobby,
    bool AllowJoinInProgress,
    int GamePort,
    int ProtocolVersion,
    ulong SchemaHash,
    uint AppId = 0,
    string Version = "",
    bool MetadataValid = true);
