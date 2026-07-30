using Mortz.Core.Match;

namespace Mortz.Core.Net;

/// <summary>Selection compares by value; Config does not, compare it through
/// MatchConfig.ToBytes.</summary>
public sealed record LobbySettings(LobbySelection Selection, MatchConfig Config);
