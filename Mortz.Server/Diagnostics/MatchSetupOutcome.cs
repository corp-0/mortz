namespace Mortz.Server.Diagnostics;

/// <summary>The live match's config, as bytes ready for the wire, plus the
/// arena size.</summary>
public readonly record struct MatchSetupOutcome(
    byte[] Config,
    int TerrainWidth,
    int TerrainHeight);
