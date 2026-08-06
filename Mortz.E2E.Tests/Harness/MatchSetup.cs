using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;

namespace Mortz.E2E.Tests.Harness;

/// <summary>The live match's rules and arena, decoded from the server.</summary>
public readonly record struct MatchSetup(
    MatchConfig Config,
    int TerrainWidth,
    int TerrainHeight);
