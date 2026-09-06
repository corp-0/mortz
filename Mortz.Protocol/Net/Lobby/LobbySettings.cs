using Mortz.Core.Match.Configuration;

namespace Mortz.Protocol.Net.Lobby;

/// <summary>Selection compares by value; Config does not, compare it through
/// MatchConfig.ToBytes.</summary>
public sealed record LobbySettings(LobbySelection Selection, MatchConfig Config);
