namespace Mortz.E2E.Protocol;

/// <summary>MatchConfig serialized via ToBytes, so a custom ruleset round-trips
/// exactly instead of field by field.</summary>
public sealed record MatchSetupResponse(
    Guid Id,
    byte[] Config,
    int TerrainWidth,
    int TerrainHeight) : E2EResponse(Id);
