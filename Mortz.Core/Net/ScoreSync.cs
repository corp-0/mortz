using Mortz.Core.Match;

namespace Mortz.Core.Net;

public sealed record ScoreSync(IReadOnlyList<ScoreRow> Rows, TeamKills TeamKills);
