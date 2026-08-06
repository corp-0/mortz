using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match;

/// <summary>Held is the new state, null when match point just lapsed.</summary>
public readonly record struct MatchPointChange(MatchPoint? Held);
