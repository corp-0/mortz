using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match.Scoring;

/// <summary>Who is closest to winning and how much they still need.</summary>
public readonly record struct MatchStanding(Victor? Leader, int Remaining);
