namespace Mortz.Core.Net;

/// <summary>The map and mode in force plus the catalogs they came from. ModeId
/// is null when the live config matches no official mode.</summary>
public sealed record LobbySelection(
    string MapId,
    string MapHash,
    LobbyCatalog Maps,
    LobbyCatalog Modes,
    string? ModeId);
