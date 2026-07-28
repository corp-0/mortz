namespace Mortz.Core.Net.Query;

/// <summary>What a server tells the browser about itself. The version fields
/// are there so an unjoinable server reads as incompatible rather than dead.</summary>
public sealed record ServerInfo(
    string Name,
    string Mode,
    string Map,
    int Players,
    int MaxPlayers,
    bool InLobby,
    int GamePort,
    int ProtocolVersion,
    ulong SchemaHash);
