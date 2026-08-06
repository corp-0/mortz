using Mortz.Content;
using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Server.Content;

namespace Mortz.Server;

/// <summary>The server's entire boot input: one immutable engine-free value a
/// test can build.</summary>
public sealed record ServerBoot
{
    public required MapSnapshot Map { get; init; }
    public required MatchConfig Rules { get; init; }
    public required ContentCatalog Catalog { get; init; }
    public required string AdminPassword { get; init; }
    public required string Name { get; init; }
    public required int GamePort { get; init; }
    public required int QueryPort { get; init; }

    /// <summary>The root of all server randomness. Chance enters the process once, at load.</summary>
    public required int Seed { get; init; }

    public required bool NetStats { get; init; }

    public required bool AllowJoinInProgress { get; init; }
}
